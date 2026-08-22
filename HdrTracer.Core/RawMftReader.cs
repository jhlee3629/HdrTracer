using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

public static class RawMftReader
{
    public struct NtfsBootSector
    {
        public ushort BytesPerSector;       
        public byte   SectorsPerCluster;    
        public uint   BytesPerCluster;      
        public ulong  TotalSectors;         
        public ulong  MftStartCluster;      
        public ulong  MftMirrorCluster;     
        public int    BytesPerMftRecord;    
        public ulong  MftStartByteOffset;   
    }

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

        byte[] buffer = new byte[512];
        if (!ReadAtOffset(handle, 0, buffer, 512))
            throw new InvalidOperationException("부트 섹터 읽기 실패");

        if (buffer[3] != (byte)'N' || buffer[4] != (byte)'T' || buffer[5] != (byte)'F' || buffer[6] != (byte)'S')
            throw new InvalidOperationException($"NTFS가 아닌 파일시스템: {driveLetter}");

        var bs = new NtfsBootSector();
        bs.BytesPerSector = BitConverter.ToUInt16(buffer, 0x0B);
        bs.SectorsPerCluster = buffer[0x0D];
        bs.BytesPerCluster = (uint)bs.BytesPerSector * bs.SectorsPerCluster;
        bs.TotalSectors = BitConverter.ToUInt64(buffer, 0x28);
        bs.MftStartCluster = BitConverter.ToUInt64(buffer, 0x30);
        bs.MftMirrorCluster = BitConverter.ToUInt64(buffer, 0x38);

        sbyte clustersPerMft = (sbyte)buffer[0x40];
        if (clustersPerMft > 0)
            bs.BytesPerMftRecord = clustersPerMft * (int)bs.BytesPerCluster;
        else
            bs.BytesPerMftRecord = 1 << -clustersPerMft;

        bs.MftStartByteOffset = bs.MftStartCluster * bs.BytesPerCluster;

        return bs;
    }

    public static bool ReadAtOffset(SafeFileHandle handle, long offset, byte[] buffer, int count)
    {
        if (!Native.SetFilePointerEx(handle, offset, out _, 0))
            return false;

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

        byte[] buffer = new byte[bs.BytesPerMftRecord];
        if (!ReadAtOffset(handle, (long)bs.MftStartByteOffset, buffer, bs.BytesPerMftRecord))
            throw new InvalidOperationException("MFT 0번 레코드 읽기 실패");

        ApplyUpdateSequenceFixup(buffer, bs.BytesPerMftRecord);

        return buffer;
    }

    public static void ApplyUpdateSequenceFixup(byte[] buffer, int recordSize)
    {
        if (buffer.Length < 8) return;

        if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0 && buffer[3] == 0)
            return; 

        ushort usaOffset = BitConverter.ToUInt16(buffer, 0x04);  // Update Sequence Array offset
        ushort usaSize   = BitConverter.ToUInt16(buffer, 0x06);  // count (n+1 개 항목, 첫 1개는 검증용)

        if (usaSize < 2) return;
        if (usaOffset + usaSize * 2 > buffer.Length) return;

        ushort expectedUsn = BitConverter.ToUInt16(buffer, usaOffset);

        int sectorSize = 512;
        for (int i = 1; i < usaSize; i++)
        {
            int sectorEnd = i * sectorSize - 2;
            if (sectorEnd + 2 > buffer.Length) break;

            ushort sectorTail = BitConverter.ToUInt16(buffer, sectorEnd);
            if (sectorTail != expectedUsn)
                return; 

            ushort realValue = BitConverter.ToUInt16(buffer, usaOffset + i * 2);
            buffer[sectorEnd]     = (byte)(realValue & 0xFF);
            buffer[sectorEnd + 1] = (byte)((realValue >> 8) & 0xFF);
        }
    }

    public enum AttrType : uint
    {
        StandardInformation = 0x10,
        AttributeList       = 0x20,
        FileName            = 0x30,
        Data                = 0x80,
        End                 = 0xFFFFFFFF
    }

    public struct MftFileInfo
    {
        public bool   IsValid;        
        public bool   IsDirectory;    
        public bool   IsHiddenSystem; 
        public string Name;           
        public ulong  ParentRef;      
        public long   Size;           
        public long   ModifiedTicks;  
        public byte   NameSpace;      
    }

    public struct AttrListEntry
    {
        public uint  AttrType;       
        public ulong BaseRecordRef;  
    }

    public static MftFileInfo ParseMftRecord(byte[] record)
    {
        var info = new MftFileInfo { Name = "" };

        if (record.Length < 0x38) return info;

        if (record[0] != (byte)'F' || record[1] != (byte)'I' ||
            record[2] != (byte)'L' || record[3] != (byte)'E')
            return info;

        ushort flags = BitConverter.ToUInt16(record, 0x16);
        bool inUse = (flags & 0x01) != 0;
        bool isDir = (flags & 0x02) != 0;

        if (!inUse) return info; 

        info.IsValid = true;
        info.IsDirectory = isDir;

        ushort firstAttrOffset = BitConverter.ToUInt16(record, 0x14);
        int pos = firstAttrOffset;

        byte bestNameSpace = 255;

        while (pos < record.Length - 8)
        {
            uint attrType = BitConverter.ToUInt32(record, pos);
            if (attrType == 0xFFFFFFFF) break;  

            if (pos + 4 >= record.Length) break;
            uint attrLength = BitConverter.ToUInt32(record, pos + 4);
            if (attrLength == 0 || attrLength > record.Length) break;

            int nextPos = pos + (int)attrLength;

            if (pos + 8 >= record.Length) break;
            byte nonResident = record[pos + 8];

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

            if (nextPos <= pos) break;
            pos = nextPos;
        }

        return info;
    }

    private static void ParseStandardInfo(byte[] record, int attrPos, ref MftFileInfo info)
    {
        if (attrPos + 0x18 >= record.Length) return;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;

        if (dataPos + 0x20 > record.Length) return;

        long modifiedFt = BitConverter.ToInt64(record, dataPos + 0x08);
        if (modifiedFt > 0)
        {
            try
            {
                info.ModifiedTicks = DateTime.FromFileTimeUtc(modifiedFt).Ticks;
            }
            catch { }
        }
    }

    private static void ParseFileName(byte[] record, int attrPos, ref MftFileInfo info, ref byte bestNameSpace)
    {
        if (attrPos + 0x18 >= record.Length) return;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        int dataPos = attrPos + contentOffset;

        if (dataPos + 0x42 > record.Length) return;

        ulong parentRef = BitConverter.ToUInt64(record, dataPos + 0x00);
        byte nameLen = record[dataPos + 0x40];
        byte nameSpace = record[dataPos + 0x41];

        int nameByteLen = nameLen * 2;
        if (dataPos + 0x42 + nameByteLen > record.Length) return;

        int priority(byte ns) => ns switch { 1 => 0, 3 => 1, 0 => 2, 2 => 3, _ => 4 };

        if (bestNameSpace == 255 || priority(nameSpace) < priority(bestNameSpace))
        {
            info.Name = System.Text.Encoding.Unicode.GetString(record, dataPos + 0x42, nameByteLen);
            info.ParentRef = parentRef & 0x0000FFFFFFFFFFFFUL; 
            info.NameSpace = nameSpace;
            bestNameSpace = nameSpace;
        }
    }

    private static void ParseDataSize(byte[] record, int attrPos, ref MftFileInfo info, byte nonResident)
    {
        if (attrPos + 0x09 < record.Length && record[attrPos + 0x09] != 0) return;

        if (nonResident == 0)
        {
            if (attrPos + 0x14 >= record.Length) return;
            uint contentLength = BitConverter.ToUInt32(record, attrPos + 0x10);
            info.Size = contentLength;
        }
        else
        {
            if (attrPos + 0x38 >= record.Length) return;
            long realSize = BitConverter.ToInt64(record, attrPos + 0x30);
            info.Size = realSize;
        }
    }

    public static List<AttrListEntry> ParseAttributeList(byte[] record, int attrPos, byte nonResident)
    {
        var entries = new List<AttrListEntry>();

        if (nonResident == 1)
        {
            return entries;
        }

        if (attrPos + 0x18 >= record.Length) return entries;
        ushort contentOffset = BitConverter.ToUInt16(record, attrPos + 0x14);
        uint contentLength = BitConverter.ToUInt32(record, attrPos + 0x10);

        int dataPos = attrPos + contentOffset;
        int dataEnd = dataPos + (int)contentLength;
        if (dataEnd > record.Length) dataEnd = record.Length;

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

    public struct DataRun
    {
        public long StartCluster; 
        public long ClusterCount; 
    }

    public static List<DataRun> ExtractMftDataRuns(byte[] mftRecord)
    {
        var runs = new List<DataRun>();

        ushort firstAttrOffset = BitConverter.ToUInt16(mftRecord, 0x14);
        int pos = firstAttrOffset;

        while (pos < mftRecord.Length - 8)
        {
            uint attrType = BitConverter.ToUInt32(mftRecord, pos);
            if (attrType == 0xFFFFFFFF) break;

            uint attrLength = BitConverter.ToUInt32(mftRecord, pos + 4);
            if (attrLength == 0 || attrLength > mftRecord.Length) break;

            byte nonResident = mftRecord[pos + 8];

            if (attrType == (uint)AttrType.Data && nonResident == 1)
            {
                ushort runListOffset = BitConverter.ToUInt16(mftRecord, pos + 0x20);
                int runPos = pos + runListOffset;
                int runEnd = pos + (int)attrLength;

                long currentCluster = 0; 

                while (runPos < runEnd && runPos < mftRecord.Length)
                {
                    byte header = mftRecord[runPos];
                    if (header == 0) break;

                    int lengthBytes = header & 0x0F;
                    int offsetBytes = (header >> 4) & 0x0F;
                    runPos++;

                    if (lengthBytes == 0 || lengthBytes > 8) break;
                    if (runPos + lengthBytes + offsetBytes > mftRecord.Length) break;

                    long runLength = 0;
                    for (int i = 0; i < lengthBytes; i++)
                        runLength |= (long)mftRecord[runPos + i] << (i * 8);
                    runPos += lengthBytes;

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
                        continue;
                    }

                    currentCluster += runOffset;

                    runs.Add(new DataRun
                    {
                        StartCluster = currentCluster,
                        ClusterCount = runLength
                    });
                }

                break; 
            }

            pos += (int)attrLength;
        }

        return runs;
    }

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

        Parallel.For(0, (int)totalRecords, i =>
        {
            ApplyUpdateSequenceFixupInPlace(allMftData, i * recordSize, recordSize);
        });

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

            AddAllFileNames(allMftData, recPos, recordSize, index, (ulong)mftIdx);
        }

        foreach (long mftIdx in deferred)
        {
            int recPos = (int)(mftIdx * recordSize);
            AddAllFileNamesDeferred(allMftData, recPos, recordSize, totalRecords, index, (ulong)mftIdx);
        }

        index.LinkParents();
        return index;
    }

    public static (FileIndex Index, ulong JournalId, long StartUsn) BuildIndexWithJournalInfo(string driveLetter)
    {
        var index = BuildIndexFromRawMft(driveLetter);

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

    private static void AddAllFileNames(byte[] data, int recPos, int recordSize, FileIndex index, ulong mftRef)
    {
        var baseInfo = ParseMftRecordAt(data, recPos);
        if (!baseInfo.IsValid) return;

        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + recordSize;

        var names = new List<(ulong parent, string name, byte ns)>();
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
                        names.Add((parentRef, name, nameSpace));
                    }
                }
            }

            pos += (int)attrLength;
        }

        var parentHasWin32 = new HashSet<ulong>();
        foreach (var (parent, _, ns) in names)
            if (ns == 1 || ns == 3) parentHasWin32.Add(parent);

        foreach (var (parent, name, ns) in names)
        {
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

    private static void AddAllFileNamesDeferred(byte[] data, int recPos, int recordSize, long totalRecords, FileIndex index, ulong mftRef)
    {
        var baseInfo = ParseMftRecordAt(data, recPos);
        if (!baseInfo.IsValid) return;

        var names = new List<(ulong parent, string name, byte ns)>();
        CollectFileNames(data, recPos, recordSize, names);

        ushort firstAttrOffset = BitConverter.ToUInt16(data, recPos + 0x14);
        int pos = recPos + firstAttrOffset;
        int end = recPos + recordSize;
        while (pos < end - 8)
        {
            uint attrType = BitConverter.ToUInt32(data, pos);
            if (attrType == 0xFFFFFFFF) break;
            uint attrLength = BitConverter.ToUInt32(data, pos + 4);
            if (attrLength == 0) break;

            if (attrType == 0x20 && data[pos + 8] == 0)  
            {
                var entries = ParseAttributeListInline(data, pos);
                var seenExtRecords = new HashSet<ulong>();
                foreach (var entry in entries)
                {
                    if (entry.AttrType != (uint)AttrType.FileName) continue;
                    if (entry.BaseRecordRef >= (ulong)totalRecords) continue;
                    if (entry.BaseRecordRef == mftRef) continue; 
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

        var parentHasWin32 = new HashSet<ulong>();
        foreach (var (parent, _, ns) in names)
            if (ns == 1 || ns == 3) parentHasWin32.Add(parent);

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

    private static bool IsValidFileRecord(byte[] data, int pos)
    {
        if (pos + 0x18 >= data.Length) return false;
        if (data[pos] != (byte)'F' || data[pos+1] != (byte)'I' ||
            data[pos+2] != (byte)'L' || data[pos+3] != (byte)'E') return false;
        ushort flags = BitConverter.ToUInt16(data, pos + 0x16);
        return (flags & 0x01) != 0;  
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
        if (data[recPos] != (byte)'F') return;

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
        var info = ParseMftRecordAt(data, recPos);

        if (string.IsNullOrEmpty(info.Name))
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
        if (attrPos + 0x09 < data.Length && data[attrPos + 0x09] != 0) return;

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
    
