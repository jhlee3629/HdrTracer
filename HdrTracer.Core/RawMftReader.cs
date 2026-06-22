using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

/// <summary>
/// NTFS MFT(Master File Table)를 직접 읽어서 모든 파일을 인덱싱.
/// USN Journal 기반보다 완전한 결과를 제공.
/// </summary>
public static class RawMftReader
{
    /// <summary>NTFS 부트 섹터의 핵심 정보.</summary>
    public struct NtfsBootSector
    {
        public ushort BytesPerSector;       // 보통 512
        public byte   SectorsPerCluster;    // 보통 8 (4KB 클러스터)
        public uint   BytesPerCluster;      // 계산값: BytesPerSector * SectorsPerCluster
        public ulong  TotalSectors;         // 디스크 전체 섹터 수
        public ulong  MftStartCluster;      // $MFT의 시작 클러스터 번호
        public ulong  MftMirrorCluster;     // $MFTMirr의 시작 클러스터
        public int    BytesPerMftRecord;    // 보통 1024
        public ulong  MftStartByteOffset;   // 계산값: MftStartCluster * BytesPerCluster
    }

    /// <summary>지정된 드라이브의 NTFS 부트 섹터를 읽어 정보 반환.</summary>
    public static NtfsBootSector ReadBootSector(string driveLetter)
    {
        var path = $@"\\.\{driveLetter}";

        using var handle = Native.CreateFile(
            path,
            Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) throw new UnauthorizedAccessException();
            throw new System.ComponentModel.Win32Exception(err);
        }

        // 첫 512바이트 읽기 (부트 섹터)
        byte[] buffer = new byte[512];
        if (!ReadAtOffset(handle, 0, buffer, 512))
            throw new InvalidOperationException("부트 섹터 읽기 실패");

        // NTFS 시그니처 확인 (OEM ID = "NTFS    ")
        if (buffer[3] != (byte)'N' || buffer[4] != (byte)'T' || buffer[5] != (byte)'F' || buffer[6] != (byte)'S')
            throw new InvalidOperationException($"NTFS가 아닌 파일시스템: {driveLetter}");

        // 부트 섹터 파싱
        // 참고: https://en.wikipedia.org/wiki/NTFS#Partition_Boot_Sector_(PBS)
        var bs = new NtfsBootSector();
        bs.BytesPerSector = BitConverter.ToUInt16(buffer, 0x0B);
        bs.SectorsPerCluster = buffer[0x0D];
        bs.BytesPerCluster = (uint)bs.BytesPerSector * bs.SectorsPerCluster;
        bs.TotalSectors = BitConverter.ToUInt64(buffer, 0x28);
        bs.MftStartCluster = BitConverter.ToUInt64(buffer, 0x30);
        bs.MftMirrorCluster = BitConverter.ToUInt64(buffer, 0x38);

        // ClustersPerMftRecord: 양수면 클러스터 단위, 음수면 2^|n| 바이트
        sbyte clustersPerMft = (sbyte)buffer[0x40];
        if (clustersPerMft > 0)
            bs.BytesPerMftRecord = clustersPerMft * (int)bs.BytesPerCluster;
        else
            bs.BytesPerMftRecord = 1 << -clustersPerMft;

        bs.MftStartByteOffset = bs.MftStartCluster * bs.BytesPerCluster;

        return bs;
    }

    /// <summary>핸들의 특정 오프셋에서 데이터 읽기. 오프셋과 길이는 섹터 정렬되어야 함.</summary>
    public static bool ReadAtOffset(SafeFileHandle handle, long offset, byte[] buffer, int count)
    {
        // 오프셋으로 이동
        if (!Native.SetFilePointerEx(handle, offset, out _, 0))
            return false;

        // 읽기
        uint bytesRead;
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                if (!Native.ReadFile(handle, (IntPtr)ptr, (uint)count, out bytesRead, IntPtr.Zero))
                    return false;
            }
        }
        return bytesRead == count;
    }

    /// <summary>MFT 0번 레코드 ($MFT 자체)를 읽는다.</summary>
    public static byte[] ReadMftRecordZero(string driveLetter, NtfsBootSector bs)
    {
        var path = $@"\\.\{driveLetter}";

        using var handle = Native.CreateFile(
            path,
            Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new InvalidOperationException("드라이브 핸들 실패");

        // raw I/O는 섹터 단위로만 가능. 1024바이트는 2섹터.
        // MftStartByteOffset이 섹터 정렬되어 있는지 확인 — NTFS는 항상 정렬됨.
        byte[] buffer = new byte[bs.BytesPerMftRecord];
        if (!ReadAtOffset(handle, (long)bs.MftStartByteOffset, buffer, bs.BytesPerMftRecord))
            throw new InvalidOperationException("MFT 0번 레코드 읽기 실패");

        // Update Sequence 보정 (NTFS의 데이터 무결성 메커니즘)
        ApplyUpdateSequenceFixup(buffer, bs.BytesPerMftRecord);

        return buffer;
    }

    /// <summary>
    /// NTFS Update Sequence Fixup 적용.
    /// NTFS는 각 섹터 마지막 2바이트에 "USN 값"을 넣어 데이터 손상을 감지/복구한다.
    /// 읽은 후엔 원래 데이터로 복원해야 한다.
    /// </summary>
    public static void ApplyUpdateSequenceFixup(byte[] buffer, int recordSize)
    {
        if (buffer.Length < 8) return;

        // 시그니처 확인 ("FILE" or "INDX" 등)
        if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0 && buffer[3] == 0)
            return; // 사용 안 함

        // USN 헤더 위치
        ushort usaOffset = BitConverter.ToUInt16(buffer, 0x04);  // Update Sequence Array offset
        ushort usaSize   = BitConverter.ToUInt16(buffer, 0x06);  // count (n+1 개 항목, 첫 1개는 검증용)

        if (usaSize < 2) return;
        if (usaOffset + usaSize * 2 > buffer.Length) return;

        // 첫 2바이트는 "예상 USN 값"
        ushort expectedUsn = BitConverter.ToUInt16(buffer, usaOffset);

        // 각 섹터(512바이트)의 마지막 2바이트가 expectedUsn과 같아야 함
        int sectorSize = 512;
        for (int i = 1; i < usaSize; i++)
        {
            int sectorEnd = i * sectorSize - 2;
            if (sectorEnd + 2 > buffer.Length) break;

            ushort sectorTail = BitConverter.ToUInt16(buffer, sectorEnd);
            if (sectorTail != expectedUsn)
                return; // 손상된 레코드 — 그냥 두기 (오류 처리는 호출자가)

            // 실제 값(USN Array의 i번째)으로 복원
            ushort realValue = BitConverter.ToUInt16(buffer, usaOffset + i * 2);
            buffer[sectorEnd]     = (byte)(realValue & 0xFF);
            buffer[sectorEnd + 1] = (byte)((realValue >> 8) & 0xFF);
        }
    }

    // === 속성 파싱 ===

    /// <summary>속성 타입.</summary>
    public enum AttrType : uint
    {
        StandardInformation = 0x10,
        AttributeList       = 0x20,
        FileName            = 0x30,
        Data                = 0x80,
        End                 = 0xFFFFFFFF
    }

    /// <summary>파일 정보. 한 MFT 레코드에서 추출한 핵심 정보.</summary>
    public struct MftFileInfo
    {
        public bool   IsValid;        // 레코드가 사용 중인지
        public bool   IsDirectory;    // 디렉토리 여부
        public bool   IsHiddenSystem; // Hidden+System 속성 여부 (예: 알약 미끼 폴더)
        public string Name;           // 파일 이름 (가장 좋은 것 선택)
        public ulong  ParentRef;      // 부모 MFT 참조 (하위 48비트만 의미 있음)
        public long   Size;           // 파일 크기
        public long   ModifiedTicks;  // 수정 시간 (DateTime.Ticks UTC)
        public byte   NameSpace;      // 0=POSIX, 1=Win32, 2=DOS, 3=Win32+DOS
    }

    /// <summary>$ATTRIBUTE_LIST의 각 항목.</summary>
    public struct AttrListEntry
    {
        public uint  AttrType;       // 가리키는 속성 타입 (0x10, 0x30, 0x80 등)
        public ulong BaseRecordRef;  // 그 속성이 들어 있는 다른 MFT 레코드 번호 (하위 48비트)
    }

    /// <summary>MFT 레코드(1024바이트)에서 파일 정보를 추출.</summary>
    public static MftFileInfo ParseMftRecord(byte[] record)
    {
        var info = new MftFileInfo { Name = "" };

        if (record.Length < 0x38) return info;

        // 시그니처 확인
        if (record[0] != (byte)'F' || record[1] != (byte)'I' ||
            record[2] != (byte)'L' || record[3] != (byte)'E')
            return info;

        // Flags
        ushort flags = BitConverter.ToUInt16(record, 0x16);
        bool inUse = (flags & 0x01) != 0;
        bool isDir = (flags & 0x02) != 0;

        if (!inUse) return info; // 삭제된 레코드

        info.IsValid = true;
        info.IsDirectory = isDir;

        // First Attribute Offset
        ushort firstAttrOffset = BitConverter.ToUInt16(record, 0x14);
        int pos = firstAttrOffset;

        // Win32 네임스페이스 이름을 우선 선택 (DOS 8.3 이름은 덮어쓰지 않게)
        byte bestNameSpace = 255;

        while (pos < record.Length - 8)
        {
            uint attrType = BitConverter.ToUInt32(record, pos);
            if (attrType == 0xFFFFFFFF) break;  // 속성 종료

            if (pos + 4 >= record.Length) break;
            uint attrLength = BitConverter.ToUInt32(record, pos + 4);
            if (attrLength == 0 || attrLength > record.Length) break;

            // 다음 속성 위치 계산 (안전성 우선)
            int nextPos = pos + (int)attrLength;

            // Resident 여부
            if (pos + 8 >= record.Length) break;
            byte nonResident = record[pos + 8];

            // 우리가 관심 있는 속성만 처리
            switch ((AttrType)attrType)
            {
                case AttrType.StandardInformation:
                    ParseStandardInfo(record, pos, ref info);
                    break;
                case AttrType.FileName:
                    ParseFileName(record, pos, ref info, ref bestNameSpace);
                    break;
                case AttrType.Data:
                    if (!isDir) ParseDataSize(record, pos, ref info, nonResident);
                    break;
            }

            if (nextPos <= pos) break; // 무한 루프 방지
            pos = nextPos;
        }

        return info;
    }

    private static void ParseStandardInfo(byte[] record, int attrPos, ref MftFileInfo info)
    {
        // Resident 속성: 헤더 16바이트 + Content
        // Content Offset @ +0x14 (헤더 안에서)
        if (attrPos + 0x18 >= record.Length) return;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;

        if (dataPos + 0x20 > record.Length) return;

        // $STD_INFO 내용:
        // 0x00: CreationTime
        // 0x08: ModificationTime (LastWriteTime)
        // 0x10: MftModifiedTime
        // 0x18: AccessTime
        long modifiedFt = BitConverter.ToInt64(record, dataPos + 0x08);
        if (modifiedFt > 0)
        {
            // FILETIME → DateTime.Ticks
            try
            {
                info.ModifiedTicks = DateTime.FromFileTimeUtc(modifiedFt).Ticks;
            }
            catch { }
        }
    }

    private static void ParseFileName(byte[] record, int attrPos, ref MftFileInfo info, ref byte bestNameSpace)
    {
        // Resident 속성: Content Offset @ +0x14
        if (attrPos + 0x18 >= record.Length) return;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;

        if (dataPos + 0x42 > record.Length) return;

        // $FILE_NAME 내용:
        // 0x00: ParentDirectoryReference (8바이트)
        // 0x40: NameLength (1바이트, 문자 수)
        // 0x41: NameSpace (1바이트)
        // 0x42: Name (UTF-16)
        ulong parentRef = BitConverter.ToUInt64(record, dataPos + 0x00);
        byte nameLen = record[dataPos + 0x40];
        byte nameSpace = record[dataPos + 0x41];

        int nameByteLen = nameLen * 2;
        if (dataPos + 0x42 + nameByteLen > record.Length) return;

        // 네임스페이스 우선순위: Win32 > Win32+DOS > POSIX > DOS
        // 더 좋은 네임스페이스를 만나면 교체. (낮은 값일수록 더 일반적인 이름)
        // POSIX=0, Win32=1, DOS=2, Win32+DOS=3
        int priority(byte ns) => ns switch { 1 => 0, 3 => 1, 0 => 2, 2 => 3, _ => 4 };

        if (bestNameSpace == 255 || priority(nameSpace) < priority(bestNameSpace))
        {
            info.Name = System.Text.Encoding.Unicode.GetString(record, dataPos + 0x42, nameByteLen);
            info.ParentRef = parentRef & 0x0000FFFFFFFFFFFFUL; // 하위 48비트만
            info.NameSpace = nameSpace;
            bestNameSpace = nameSpace;
        }
    }

    private static void ParseDataSize(byte[] record, int attrPos, ref MftFileInfo info, byte nonResident)
    {
        if (nonResident == 0)
        {
            // Resident: Content가 레코드 안에 직접 있음. Content Length가 곧 파일 크기.
            if (attrPos + 0x14 >= record.Length) return;
            uint contentLength = BitConverter.ToUInt32(record, attrPos + 0x10);
            info.Size = contentLength;
        }
        else
        {
            // Non-resident: RealSize @ +0x30
            if (attrPos + 0x38 >= record.Length) return;
            long realSize = BitConverter.ToInt64(record, attrPos + 0x30);
            info.Size = realSize;
        }
    }

    /// <summary>$ATTRIBUTE_LIST 속성을 파싱해서 외부 레코드 참조 목록을 반환.</summary>
    public static List<AttrListEntry> ParseAttributeList(byte[] record, int attrPos, byte nonResident)
    {
        var entries = new List<AttrListEntry>();

        if (nonResident == 1)
        {
            // Non-resident인 경우는 일단 무시 (드뭄, 큰 작업)
            // 실제로는 디스크에서 추가로 읽어야 하지만, 일단 resident만 처리
            return entries;
        }

        // Resident 속성
        if (attrPos + 0x18 >= record.Length) return entries;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        uint contentLength = BitConverter.ToUInt32(record, attrPos + 0x10);

        int dataPos = attrPos + contentOffset;
        int dataEnd = dataPos + (int)contentLength;
        if (dataEnd > record.Length) dataEnd = record.Length;

        // $ATTRIBUTE_LIST 안의 각 항목:
        // 0x00: Type (4바이트)
        // 0x04: Record Length (2바이트) ← 이 항목의 길이
        // 0x06: Name Length (1바이트)
        // 0x07: Name Offset (1바이트)
        // 0x08: Starting VCN (8바이트)
        // 0x10: Base Record File Reference (8바이트) ← 여기에 외부 MFT 번호
        // 0x18: Attribute ID (2바이트)
        // ... name (있다면)

        while (dataPos + 0x18 < dataEnd)
        {
            uint type = BitConverter.ToUInt32(record, dataPos);
            if (type == 0xFFFFFFFF || type == 0) break;

            ushort entryLength = BitConverter.ToUInt16(record, dataPos + 0x04);
            if (entryLength == 0) break;

            ulong baseRef = BitConverter.ToUInt64(record, dataPos + 0x10);

            entries.Add(new AttrListEntry
            {
                AttrType = type,
                BaseRecordRef = baseRef & 0x0000FFFFFFFFFFFFUL
            });

            dataPos += entryLength;
        }

        return entries;
    }

    /// <summary>디스크상의 한 연속된 영역.</summary>
    public struct DataRun
    {
        public long StartCluster;   // 시작 클러스터 번호
        public long ClusterCount;   // 길이 (클러스터 개수)
    }

    /// <summary>
    /// $MFT 레코드의 $DATA 속성에서 Data Runs를 추출.
    /// 반환: 디스크상 MFT가 흩어져 있는 위치들.
    /// </summary>
    public static List<DataRun> ExtractMftDataRuns(byte[] mftRecord)
    {
        var runs = new List<DataRun>();

        // First Attribute Offset
        ushort firstAttrOffset = BitConverter.ToUInt16(mftRecord, 0x14);
        int pos = firstAttrOffset;

        // 속성 체인 순회하면서 Non-resident $DATA 찾기
        while (pos < mftRecord.Length - 8)
        {
            uint attrType = BitConverter.ToUInt32(mftRecord, pos);
            if (attrType == 0xFFFFFFFF) break;

            uint attrLength = BitConverter.ToUInt32(mftRecord, pos + 4);
            if (attrLength == 0 || attrLength > mftRecord.Length) break;

            byte nonResident = mftRecord[pos + 8];

            if (attrType == (uint)AttrType.Data && nonResident == 1)
            {
                // Non-resident 속성의 헤더 구조:
                // 0x10: Starting VCN (8바이트)
                // 0x18: Last VCN (8바이트)
                // 0x20: Data Run Offset (2바이트) — 속성 시작 위치 기준
                // ...
                ushort runListOffset = BitConverter.ToUInt16(mftRecord, pos + 0x20);
                int runPos = pos + runListOffset;
                int runEnd = pos + (int)attrLength;

                // Data Runs 디코딩
                long currentCluster = 0;  // 누적 절대 위치

                while (runPos < runEnd && runPos < mftRecord.Length)
                {
                    byte header = mftRecord[runPos];
                    if (header == 0) break;  // 종료 마커

                    int lengthBytes = header & 0x0F;
                    int offsetBytes = (header >> 4) & 0x0F;
                    runPos++;

                    if (lengthBytes == 0 || lengthBytes > 8) break;
                    if (runPos + lengthBytes + offsetBytes > mftRecord.Length) break;

                    // Length 읽기 (unsigned)
                    long runLength = 0;
                    for (int i = 0; i < lengthBytes; i++)
                        runLength |= (long)mftRecord[runPos + i] << (i * 8);
                    runPos += lengthBytes;

                    // Offset 읽기 (signed, 이전 run으로부터 상대값)
                    long runOffset = 0;
                    if (offsetBytes > 0)
                    {
                        for (int i = 0; i < offsetBytes; i++)
                            runOffset |= (long)mftRecord[runPos + i] << (i * 8);
                        // 부호 확장
                        int signBit = (offsetBytes * 8) - 1;
                        if ((runOffset & (1L << signBit)) != 0)
                        {
                            long mask = -1L << (signBit + 1);
                            runOffset |= mask;
                        }
                        runPos += offsetBytes;
                    }
                    else
                    {
                        // 희소(sparse) run — 데이터 없음. 스킵.
                        continue;
                    }

                    currentCluster += runOffset;

                    runs.Add(new DataRun
                    {
                        StartCluster = currentCluster,
                        ClusterCount = runLength
                    });
                }

                break; // $DATA 하나만 처리하면 됨
            }

            pos += (int)attrLength;
        }

        return runs;
    }

    /// <summary>
    /// MFT 전체를 raw로 읽어 FileIndex를 빌드. $ATTRIBUTE_LIST도 처리.
    /// </summary>
    public static FileIndex BuildIndexFromRawMft(string driveLetter, IProgress<int>? progress = null)
    {
        var bs = ReadBootSector(driveLetter);
        var mftRecord = ReadMftRecordZero(driveLetter, bs);
        var runs = ExtractMftDataRuns(mftRecord);

        if (runs.Count == 0)
            throw new InvalidOperationException("$MFT의 Data Runs를 찾을 수 없음");

        var path = $@"\\.\{driveLetter}";
        using var handle = Native.CreateFile(
            path, Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new UnauthorizedAccessException("드라이브 핸들 실패");

        int recordSize = bs.BytesPerMftRecord;

        // 전체 MFT 데이터를 메모리에 로드
        long totalMftBytes = 0;
        foreach (var run in runs) totalMftBytes += run.ClusterCount * bs.BytesPerCluster;
        byte[] allMftData = new byte[totalMftBytes];
        long writePos = 0;

        const int ChunkBytes = 4 * 1024 * 1024;
        byte[] chunkBuffer = new byte[ChunkBytes];

        foreach (var run in runs)
        {
            long byteOffset = run.StartCluster * bs.BytesPerCluster;
            long bytesRemaining = run.ClusterCount * bs.BytesPerCluster;

            while (bytesRemaining > 0)
            {
                int readSize = (int)Math.Min(bytesRemaining, ChunkBytes);
                if (!ReadAtOffset(handle, byteOffset, chunkBuffer, readSize))
                    break;

                Buffer.BlockCopy(chunkBuffer, 0, allMftData, (int)writePos, readSize);
                writePos += readSize;
                byteOffset += readSize;
                bytesRemaining -= readSize;
            }
        }

        long totalRecords = totalMftBytes / recordSize;

        // 모든 레코드에 USA fixup 적용 (병렬)
        Parallel.For(0, (int)totalRecords, i =>
        {
            ApplyUpdateSequenceFixupInPlace(allMftData, i * recordSize, recordSize);
        });

        // === 1차 패스 ===
        var index = new FileIndex();
        index.DriveLetter = driveLetter;
        var deferred = new List<long>();

        for (long mftIdx = 0; mftIdx < totalRecords; mftIdx++)
        {
            int recPos = (int)(mftIdx * recordSize);

            if (!IsValidFileRecord(allMftData, recPos)) continue;

            if (HasAttributeList(allMftData, recPos))
            {
                deferred.Add(mftIdx);
                continue;
            }

            // 한 레코드의 모든 $FILE_NAME을 별도 항목으로 추가 (하드링크 지원)
            AddAllFileNames(allMftData, recPos, recordSize, index, (ulong)mftIdx);
        }

        // === 2차 패스 ===
        foreach (long mftIdx in deferred)
        {
            int recPos = (int)(mftIdx * recordSize);
            AddAllFileNamesDeferred(allMftData, recPos, recordSize, totalRecords, index, (ulong)mftIdx);
        }

        index.LinkParents();
        return index;
    }

    /// <summary>
    /// Raw MFT로 인덱스 빌드 + USN Journal 정보까지 한 번에 가져옴.
    /// 드라이브의 raw MFT를 읽어 인덱스를 빌드한다.
    /// </summary>
    public static (FileIndex Index, ulong JournalId, long StartUsn) BuildIndexWithJournalInfo(string driveLetter)
    {
        // 1. Raw MFT로 완전한 인덱스 빌드
        var index = BuildIndexFromRawMft(driveLetter);

        // 2. USN Journal 정보 가져오기 (실시간 갱신용)
        ulong journalId = 0;
        long startUsn = 0;

        var path = $@"\\.\{driveLetter}";
        using var handle = Native.CreateFile(
            path, Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            if (Native.DeviceIoControlQuery(
                handle, Native.FSCTL_QUERY_USN_JOURNAL,
                IntPtr.Zero, 0,
                out var jdata, (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.USN_JOURNAL_DATA_V0>(),
                out _, IntPtr.Zero))
            {
                journalId = jdata.UsnJournalID;
                startUsn = jdata.NextUsn;
            }
            else
            {
                // 저널 없으면 생성
                var createData = new Native.CREATE_USN_JOURNAL_DATA
                {
                    MaximumSize = 32 * 1024 * 1024,
                    AllocationDelta = 4 * 1024 * 1024
                };
                Native.DeviceIoControlCreateJournal(
                    handle, Native.FSCTL_CREATE_USN_JOURNAL,
                    ref createData,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.CREATE_USN_JOURNAL_DATA>(),
                    IntPtr.Zero, 0, out _, IntPtr.Zero);

                // 다시 시도
                if (Native.DeviceIoControlQuery(
                    handle, Native.FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero, 0,
                    out var jdata2, (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.USN_JOURNAL_DATA_V0>(),
                    out _, IntPtr.Zero))
                {
                    journalId = jdata2.UsnJournalID;
                    startUsn = jdata2.NextUsn;
                }
            }
        }

        return (index, journalId, startUsn);
    }

    /// <summary>한 레코드의 모든 $FILE_NAME을 인덱스에 추가 (하드링크 지원).</summary>
    private static void AddAllFileNames(byte[] data, int recPos, int recordSize, FileIndex index, ulong mftRef)
    {
        // 기본 정보 (Standard Info + Data 크기)
        var baseInfo = ParseMftRecordAt(data, recPos);
        if (!baseInfo.IsValid) return;

        // 모든 $FILE_NAME 속성 순회
        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + recordSize;

        // DOS 8.3 short name은 보통 같은 디렉토리의 중복이므로 한 디렉토리당 한 이름만.
        // 다른 디렉토리(다른 ParentRef)에 있는 모든 이름은 다 추가.

        // 1차: 모든 (parent, name, namespace) 수집
        var names = new List<(ulong parent, string name, byte ns)>();
        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0) break;

            if (attrType == 0x30)  // $FILE_NAME
            {
                ushort contentOffset = BitConverter.ToUInt16(data, pos + 0x14);
                int dataPos = pos + contentOffset;
                if (dataPos + 0x42 < data.Length)
                {
                    ulong parentRef = BitConverter.ToUInt64(data, dataPos + 0x00) & 0x0000FFFFFFFFFFFFUL;
                    byte nameLen = data[dataPos + 0x40];
                    byte nameSpace = data[dataPos + 0x41];
                    int nameByteLen = nameLen * 2;
                    if (dataPos + 0x42 + nameByteLen <= data.Length)
                    {
                        string name = System.Text.Encoding.Unicode.GetString(data, dataPos + 0x42, nameByteLen);
                        names.Add((parentRef, name, nameSpace));
                    }
                }
            }

            pos += (int)attrLength;
        }

        // 2차: 같은 parent에서 DOS-only 이름은 Win32/Win32+DOS 이름이 있으면 제거
        var parentHasWin32 = new HashSet<ulong>();
        foreach (var (parent, _, ns) in names)
            if (ns == 1 || ns == 3) parentHasWin32.Add(parent);

        // 3차: 인덱스에 추가
        foreach (var (parent, name, ns) in names)
        {
            // DOS 8.3만 있는 이름은 같은 parent에 Win32 이름이 있으면 스킵
            if (ns == 2 && parentHasWin32.Contains(parent)) continue;

            unsafe
            {
                fixed (char* nptr = name)
                {
                    index.Add(nptr, name.Length, mftRef, parent, baseInfo.IsDirectory, baseInfo.Size, baseInfo.IsHiddenSystem);
                }
            }
            if (baseInfo.ModifiedTicks > 0)
            {
                index.SetMetadata(index.Count - 1, baseInfo.Size, new DateTime(baseInfo.ModifiedTicks, DateTimeKind.Utc));
            }
        }
    }

    /// <summary>$ATTRIBUTE_LIST가 있는 레코드의 모든 $FILE_NAME을 추가.</summary>
    private static void AddAllFileNamesDeferred(byte[] data, int recPos, int recordSize, long totalRecords, FileIndex index, ulong mftRef)
    {
        // 기본 정보 (이 레코드에서 가능한 만큼)
        var baseInfo = ParseMftRecordAt(data, recPos);
        if (!baseInfo.IsValid) return;

        // 이 레코드의 $FILE_NAME도 다 가져옴
        var names = new List<(ulong parent, string name, byte ns)>();
        CollectFileNames(data, recPos, recordSize, names);

        // $ATTRIBUTE_LIST 따라가서 외부 레코드들의 $FILE_NAME도 가져옴
        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + recordSize;
        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0) break;

            if (attrType == 0x20 && data[pos + 8] == 0)  // Resident ATTRIBUTE_LIST
            {
                var entries = ParseAttributeListInline(data, pos);
                var seenExtRecords = new HashSet<ulong>();
                foreach (var entry in entries)
                {
                    if (entry.AttrType != (uint)AttrType.FileName) continue;
                    if (entry.BaseRecordRef >= (ulong)totalRecords) continue;
                    if (entry.BaseRecordRef == mftRef) continue;  // 자기 자신은 이미 처리
                    if (!seenExtRecords.Add(entry.BaseRecordRef)) continue;

                    int extRecPos = (int)(entry.BaseRecordRef * (ulong)recordSize);
                    if (extRecPos + recordSize > data.Length) continue;

                    CollectFileNames(data, extRecPos, recordSize, names);
                }
                break;
            }
            pos += (int)attrLength;
        }

        if (names.Count == 0) return;

        // DOS-only 필터링
        var parentHasWin32 = new HashSet<ulong>();
        foreach (var (parent, _, ns) in names)
            if (ns == 1 || ns == 3) parentHasWin32.Add(parent);

        // 중복 제거 (같은 parent+name 조합은 한 번만)
        var added = new HashSet<(ulong, string)>();
        foreach (var (parent, name, ns) in names)
        {
            if (ns == 2 && parentHasWin32.Contains(parent)) continue;
            var key = (parent, name);
            if (!added.Add(key)) continue;

            unsafe
            {
                fixed (char* nptr = name)
                {
                    index.Add(nptr, name.Length, mftRef, parent, baseInfo.IsDirectory, baseInfo.Size, baseInfo.IsHiddenSystem);
                }
            }
            if (baseInfo.ModifiedTicks > 0)
            {
                index.SetMetadata(index.Count - 1, baseInfo.Size, new DateTime(baseInfo.ModifiedTicks, DateTimeKind.Utc));
            }
        }
    }

    /// <summary>한 레코드에서 모든 $FILE_NAME 정보를 수집.</summary>
    private static void CollectFileNames(byte[] data, int recPos, int recordSize, List<(ulong parent, string name, byte ns)> result)
    {
        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + recordSize;

        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0) break;

            if (attrType == 0x30)
            {
                ushort contentOffset = BitConverter.ToUInt16(data, pos + 0x14);
                int dataPos = pos + contentOffset;
                if (dataPos + 0x42 < data.Length)
                {
                    ulong parentRef = BitConverter.ToUInt64(data, dataPos + 0x00) & 0x0000FFFFFFFFFFFFUL;
                    byte nameLen = data[dataPos + 0x40];
                    byte nameSpace = data[dataPos + 0x41];
                    int nameByteLen = nameLen * 2;
                    if (dataPos + 0x42 + nameByteLen <= data.Length)
                    {
                        string name = System.Text.Encoding.Unicode.GetString(data, dataPos + 0x42, nameByteLen);
                        result.Add((parentRef, name, nameSpace));
                    }
                }
            }

            pos += (int)attrLength;
        }
    }

    private static void sampleAppendNoName(System.Text.StringBuilder sb, long mftIdx, byte alNonResident, byte[] data, int recPos, int recordSize)
    {
        sb.AppendLine($"  MFT#{mftIdx}: AL_NonResident={alNonResident}");
    }

    // === 헬퍼 메서드들 ===

    private static bool IsValidFileRecord(byte[] data, int pos)
    {
        if (pos + 0x18 >= data.Length) return false;
        if (data[pos] != (byte)'F' || data[pos+1] != (byte)'I' ||
            data[pos+2] != (byte)'L' || data[pos+3] != (byte)'E') return false;
        ushort flags = BitConverter.ToUInt16(data, pos + 0x16);
        return (flags & 0x01) != 0;  // 사용 중
    }

    private static bool HasAttributeList(byte[] data, int recPos)
    {
        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int p = recPos + firstAttrOffset;
        int end = recPos + 1024;  // recordSize

        while (p < end - 8)
        {
            uint type = BitConverter.ToUInt32(data, p);
            if (type == 0xFFFFFFFF) break;
            if (type == 0x20) return true;

            uint len = BitConverter.ToUInt32(data, p + 4);
            if (len == 0) break;
            p += (int)len;
        }
        return false;
    }

    private static void ApplyUpdateSequenceFixupInPlace(byte[] data, int recPos, int recordSize)
    {
        if (recPos + recordSize > data.Length) return;
        if (data[recPos] != (byte)'F') return; // 시그니처 없으면 스킵

        ushort usaOffset = BitConverter.ToUInt16(data, recPos + 0x04);
        ushort usaSize   = BitConverter.ToUInt16(data, recPos + 0x06);
        if (usaSize < 2) return;
        if (recPos + usaOffset + usaSize * 2 > data.Length) return;

        ushort expectedUsn = BitConverter.ToUInt16(data, recPos + usaOffset);
        int sectorSize = 512;
        for (int i = 1; i < usaSize; i++)
        {
            int sectorEnd = recPos + i * sectorSize - 2;
            if (sectorEnd + 2 > recPos + recordSize) break;
            ushort sectorTail = BitConverter.ToUInt16(data, sectorEnd);
            if (sectorTail != expectedUsn) return;

            ushort realValue = BitConverter.ToUInt16(data, recPos + usaOffset + i * 2);
            data[sectorEnd]     = (byte)(realValue & 0xFF);
            data[sectorEnd + 1] = (byte)((realValue >> 8) & 0xFF);
        }
    }

    private static MftFileInfo ParseMftRecordAt(byte[] data, int recPos)
    {
        // ParseMftRecord와 같지만 원본 배열에서 직접 읽음
        var info = new MftFileInfo { Name = "" };

        ushort flags = BitConverter.ToUInt16(data, recPos + 0x16);
        info.IsValid = (flags & 0x01) != 0;
        info.IsDirectory = (flags & 0x02) != 0;
        if (!info.IsValid) return info;

        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + 1024;
        byte bestNameSpace = 255;

        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;

            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0 || pos + attrLength > recPos + 1024) break;

            byte nonResident = data[pos + 8];

            switch ((AttrType)attrType)
            {
                case AttrType.StandardInformation:
                    ParseStandardInfoAt(data, pos, ref info);
                    break;
                case AttrType.FileName:
                    ParseFileNameAt(data, pos, ref info, ref bestNameSpace);
                    break;
                case AttrType.Data:
                    if (!info.IsDirectory) ParseDataSizeAt(data, pos, ref info, nonResident);
                    break;
            }

            pos += (int)attrLength;
        }

        return info;
    }

    private static MftFileInfo ParseDeferredRecord(byte[] data, int recPos, int recordSize, long totalRecords)
    {
        // 기본 정보를 본 레코드에서 추출
        var info = ParseMftRecordAt(data, recPos);

        // 이름/부모를 못 찾았으면 ATTRIBUTE_LIST 따라가서 외부 레코드 확인
        if (string.IsNullOrEmpty(info.Name))
        {
            // $ATTRIBUTE_LIST 찾기
            ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
            int pos = recPos + firstAttrOffset;
            int end = recPos + recordSize;

            while (pos < end - 8)
            {
                uint attrType = BitConverter.ToUInt32(data, pos);
                if (attrType == 0xFFFFFFFF) break;
                uint attrLength = BitConverter.ToUInt32(data, pos + 4);
                if (attrLength == 0) break;

                if (attrType == 0x20)
                {
                    byte nonResident = data[pos + 8];
                    if (nonResident == 0)
                    {
                        var listEntries = ParseAttributeListInline(data, pos);
                        foreach (var entry in listEntries)
                        {
                            if (entry.AttrType == (uint)AttrType.FileName && entry.BaseRecordRef < (ulong)totalRecords)
                            {
                                // 다른 레코드에서 $FILE_NAME 가져오기
                                int extRecPos = (int)(entry.BaseRecordRef * (ulong)recordSize);
                                if (extRecPos + recordSize > data.Length) continue;

                                byte bestNs = 255;
                                ParseFileNameFromRecord(data, extRecPos, ref info, ref bestNs);
                                if (!string.IsNullOrEmpty(info.Name)) break;
                            }
                        }
                    }
                    break;
                }
                pos += (int)attrLength;
            }
        }

        return info;
    }

    private static List<AttrListEntry> ParseAttributeListInline(byte[] data, int attrPos)
    {
        var entries = new List<AttrListEntry>();
        ushort contentOffset = BitConverter.ToUInt16(data, attrPos + 0x14);
        uint contentLength = BitConverter.ToUInt32(data, attrPos + 0x10);

        int dataPos = attrPos + contentOffset;
        int dataEnd = dataPos + (int)contentLength;
        if (dataEnd > data.Length) dataEnd = data.Length;

        while (dataPos + 0x18 < dataEnd)
        {
            uint type = BitConverter.ToUInt32(data, dataPos);
            if (type == 0xFFFFFFFF || type == 0) break;
            ushort entryLength = BitConverter.ToUInt16(data, dataPos + 0x04);
            if (entryLength == 0) break;
            ulong baseRef = BitConverter.ToUInt64(data, dataPos + 0x10);

            entries.Add(new AttrListEntry
            {
                AttrType = type,
                BaseRecordRef = baseRef & 0x0000FFFFFFFFFFFFUL
            });
            dataPos += entryLength;
        }
        return entries;
    }

    private static void ParseFileNameFromRecord(byte[] data, int recPos, ref MftFileInfo info, ref byte bestNameSpace)
    {
        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + 1024;

        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0) break;

            if (attrType == 0x30)
                ParseFileNameAt(data, pos, ref info, ref bestNameSpace);

            pos += (int)attrLength;
        }
    }

    private static void ParseStandardInfoAt(byte[] data, int attrPos, ref MftFileInfo info)
    {
        ushort contentOffset = BitConverter.ToUInt16(data, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;
        if (dataPos + 0x24 > data.Length) return;
        long modifiedFt = BitConverter.ToInt64(data, dataPos + 0x08);
        if (modifiedFt > 0)
        {
            try { info.ModifiedTicks = DateTime.FromFileTimeUtc(modifiedFt).Ticks; }
            catch { }
        }
        // FileAttributes @ +0x20 (DWORD). Hidden=0x2, System=0x4.
        // 둘 다 있을 때만 숨김+시스템으로 본다 (예: 알약 미끼 폴더 !!QAdC).
        uint fileAttrs = BitConverter.ToUInt32(data, dataPos + 0x20);
        if ((fileAttrs & 0x2) != 0 && (fileAttrs & 0x4) != 0)
            info.IsHiddenSystem = true;
    }

    private static void ParseFileNameAt(byte[] data, int attrPos, ref MftFileInfo info, ref byte bestNameSpace)
    {
        ushort contentOffset = BitConverter.ToUInt16(data, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;
        if (dataPos + 0x42 > data.Length) return;

        ulong parentRef = BitConverter.ToUInt64(data, dataPos + 0x00);
        byte nameLen = data[dataPos + 0x40];
        byte nameSpace = data[dataPos + 0x41];
        int nameByteLen = nameLen * 2;
        if (dataPos + 0x42 + nameByteLen > data.Length) return;

        int priority(byte ns) => ns switch { 1 => 0, 3 => 1, 0 => 2, 2 => 3, _ => 4 };

        if (bestNameSpace == 255 || priority(nameSpace) < priority(bestNameSpace))
        {
            info.Name = System.Text.Encoding.Unicode.GetString(data, dataPos + 0x42, nameByteLen);
            info.ParentRef = parentRef & 0x0000FFFFFFFFFFFFUL;
            info.NameSpace = nameSpace;
            bestNameSpace = nameSpace;
        }
    }

    private static void ParseDataSizeAt(byte[] data, int attrPos, ref MftFileInfo info, byte nonResident)
    {
        if (nonResident == 0)
        {
            if (attrPos + 0x14 >= data.Length) return;
            uint contentLength = BitConverter.ToUInt32(data, attrPos + 0x10);
            info.Size = contentLength;
        }
        else
        {
            if (attrPos + 0x38 >= data.Length) return;
            long realSize = BitConverter.ToInt64(data, attrPos + 0x30);
            info.Size = realSize;
        }
    }

    private static void AddToIndex(FileIndex index, MftFileInfo info, ulong mftRef)
    {
        if (string.IsNullOrEmpty(info.Name)) return;
        unsafe
        {
            fixed (char* namePtr = info.Name)
            {
                index.Add(namePtr, info.Name.Length, mftRef, info.ParentRef,
                        info.IsDirectory, info.Size, info.IsHiddenSystem);
            }
        }
        if (info.ModifiedTicks > 0)
        {
            int idx = index.Count - 1;
            index.SetMetadata(idx, info.Size, new DateTime(info.ModifiedTicks, DateTimeKind.Utc));
        }
    }
}
    
