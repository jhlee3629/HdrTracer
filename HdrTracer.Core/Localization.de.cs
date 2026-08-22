namespace HdrTracer.Core;

public static partial class Localization
{
    internal static readonly Dictionary<string, string> _de = new()
    {
        ["menu.settings"]     = "Einstellungen",
        ["menu.refresh"]      = "Index neu aufbauen",
        ["menu.resetCols"]    = "Spaltenbreiten zurücksetzen",
        ["menu.zoom"]         = "Zoom",
        ["menu.zoom.in"]      = "Vergrößern",
        ["menu.zoom.out"]     = "Verkleinern",
        ["menu.zoom.reset"]   = "Zoom zurücksetzen",
        ["menu.shortcuts"]    = "Tastenkürzel",
        ["menu.about"]        = "Über",
        ["menu.language"]     = "Sprache",
        ["menu.lang.ko"]      = "Koreanisch",
        ["menu.lang.en"]      = "Englisch",
        ["menu.lang.zh"]      = "Chinesisch (vereinfacht)",
        ["menu.lang.ja"]      = "Japanisch",
        ["menu.lang.es"]      = "Spanisch",
        ["menu.lang.de"]      = "Deutsch",
        ["menu.lang.fr"]      = "Französisch",
        ["menu.searchHelp"]   = "Suchhilfe",
        ["menu.export"]       = "Ergebnisse exportieren (CSV)",
        ["ctx.export"]        = "Auswahl exportieren",
        ["export.done"]       = "{0} Einträge als CSV gespeichert.",
        ["menu.filter"]       = "Filter",

        ["tip.menu"]     = "Menü",
        ["tip.minimize"] = "Minimieren",
        ["tip.maximize"] = "Maximieren",
        ["tip.close"]    = "Schließen",
        ["tip.search"]   = "Suchen",
        ["tip.delete"]   = "Löschen",
        ["tip.pin"]      = "Anheften / Lösen",

        ["search.placeholder"] = "Dateinamen eingeben und Enter drücken",
        ["empty.title"] = "Keine Ergebnisse",
        ["empty.body"]  = "Kürzeren oder anderen Suchbegriff versuchen\nMehrere Wörter treffen nur Einträge, die alle enthalten\nHilfe: Menü (HdrTracer ▼) → Suchhilfe",

        ["ctx.open"]           = "Öffnen",
        ["ctx.runAsAdmin"]     = "Als Administrator ausführen",
        ["ctx.openWith"]       = "Öffnen mit...",
        ["ctx.reveal"]         = "Im Ordner anzeigen",
        ["ctx.searchFolder"]   = "Nur in diesem Ordner suchen",
        ["ctx.copyPath"]       = "Pfad kopieren",
        ["ctx.copyName"]       = "Namen kopieren",
        ["ctx.rename"]         = "Umbenennen",
        ["ctx.copyFile"]       = "Datei kopieren",
        ["ctx.delete"]         = "In den Papierkorb",
        ["ctx.properties"]     = "Eigenschaften",
        ["ctx.rename.title"]   = "Umbenennen",
        ["ctx.rename.prompt"]  = "Neuer Name:",
        ["ctx.rename.extWarn"] = "Das Ändern der Dateiendung kann die Datei unbrauchbar machen.\nTrotzdem fortfahren?",
        ["ctx.delete.confirm"] = "Diese Datei in den Papierkorb verschieben?",
        ["ctx.delete.more"]    = "…und {0} weitere",
        ["ctx.delete.danger"]  = "⚠️ Achtung: systemkritische Einträge sind dabei. Ein Löschen kann das System instabil machen.",
        ["ctx.delete.confirm.multi"] = "Die {0} ausgewählten Einträge in den Papierkorb verschieben?",
        ["ctx.delete.done.multi"]    = "{0} Einträge in den Papierkorb verschoben.",
        ["ctx.delete.partial"] = "{0} erfolgreich, {1} fehlgeschlagen",
        ["ctx.copyFile.multi"] = "{0} Dateien in die Zwischenablage kopiert",
        ["ctx.copyFile.none"]  = "Keine Dateien zum Kopieren",
        ["ctx.copyPath.multi"] = "{0} Pfade kopiert",
        ["ctx.copyName.multi"] = "{0} Namen kopiert",
        ["ctx.open.multi"]     = "{0} Einträge geöffnet",
        ["ctx.open.partial"]   = "{0} geöffnet, {1} fehlgeschlagen",
        ["ctx.delete.title"]   = "In den Papierkorb",
        ["ctx.error"]          = "Fehler",
        ["ctx.notExecutable"]  = "Keine ausführbare Datei",

        ["filter.doc"]         = "Dokumente",
        ["filter.img"]         = "Bilder",
        ["filter.media"]       = "Medien",
        ["filter.exe"]         = "Programme",
        ["filter.zip"]         = "Archive",
        ["filter.size"]        = "Größe",
        ["filter.size.10mb"]   = "Über 10MB",
        ["filter.size.100mb"]  = "Über 100MB",
        ["filter.size.1gb"]    = "Über 1GB",
        ["filter.date"]        = "Datum",
        ["filter.date.today"]  = "Heute",
        ["filter.date.week"]   = "Letzte Woche",
        ["filter.date.month"]  = "Letzter Monat",
        ["filter.date.year"]   = "Letztes Jahr",
        ["filter.clear"]       = "Filter löschen",
        ["filter.kind"]        = "Art",
        ["filter.kind.folder"] = "Nur Ordner",
        ["filter.kind.file"]   = "Nur Dateien",

        ["col.drive"]  = "LW",
        ["col.name"]   = "Name",
        ["col.path"]   = "Pfad",
        ["col.size"]   = "Größe",
        ["col.date"]   = "Geändert am",
        ["status.indexing"]         = "Laufwerke werden indiziert...",
        ["status.total"]            = "Gesamt",
        ["status.items"]            = " Einträge",
        ["status.results"]          = "Ergebnisse",
        ["status.refreshDone"]      = "Index neu aufgebaut",
        ["status.selected"]         = "{0} ausgewählt, {1}",
        ["status.selectedMany"]     = "{0:N0} ausgewählt",
        ["status.indexingProgress"] = "Indiziere… {0} s vergangen  ·  {1}",
        ["status.driveDone"]        = "{0} fertig ({1:N0})",
        ["status.driveWorking"]     = "{0} läuft",

        ["tray.open"]         = "HdrTracer öffnen",
        ["tray.exit"]         = "Beenden",
        ["tray.pinned"]       = "Angeheftete Suchen",
        ["tray.pinned.empty"] = "Keine angehefteten Suchen",
        ["tray.settings"]     = "Einstellungen",

        ["settings.title"]     = "Einstellungen",
        ["settings.indexing"]  = "Indizierung",
        ["settings.usb"]       = "Wechseldatenträger indizieren",
        ["settings.usb.desc"]  = "Dateien auf USB-Laufwerken in die Suche einbeziehen.",
        ["settings.tray"]      = "Beim Schließen in den Infobereich",
        ["settings.tray.desc"] = "Die Schaltfläche X blendet in den Infobereich aus, statt zu beenden. Beenden per Rechtsklick auf das Symbol.",
        ["settings.hidden"]      = "Versteckte und Systemobjekte anzeigen",
        ["settings.hidden.desc"] = "Versteckte + System-Objekte (z. B. Schutzordner von Antivirenprogrammen) in den Ergebnissen anzeigen.",
        ["settings.ok"]        = "OK",
        ["settings.usbOn"]     = "USB-Laufwerke werden nach dem Klick auf OK indiziert.",
        ["settings.usbOff"]    = "Indizierte USB-Daten werden nach dem Klick auf OK aus dem Speicher entfernt.",
        ["settings.autostart"]      = "Mit Windows starten",
        ["settings.autostart.desc"] = "Startet bei der Anmeldung automatisch im Infobereich (Aufgabenplanung, ohne UAC-Abfrage).",
        ["settings.autostart.fail"] = "Autostart konnte nicht geändert werden.",
        ["settings.excluded"]      = "Von der Suche ausgeschlossene Ordnernamen",
        ["settings.excluded.desc"] = "Mit Semikolon (;) trennen. Ordner mit diesen Namen und ihr gesamter Inhalt werden ausgeblendet. Z. B.: WinSxS; node_modules",
        ["settings.hotkey"]        = "Globales Tastenkürzel (Win+Alt+S)",
        ["settings.hotkey.desc"]   = "Fenster aus jeder App mit Win+Alt+S holen. Mit Esc wieder ausblenden.",
        ["settings.restoreSearch"]      = "Letzte Suche beim Start wiederherstellen",
        ["settings.restoreSearch.desc"] = "Beim erneuten Öffnen wird die letzte Suche gleich ausgeführt.",
        ["hotkey.fail"]            = "Globales Tastenkürzel (Win+Alt+S) konnte nicht registriert werden — evtl. von einem anderen Programm belegt.",

        ["sc.title"]        = "Tastenkürzel",
        ["sc.appMenu"]      = "App-Menü",
        ["sc.openSettings"] = "Einstellungen öffnen",
        ["sc.refresh"]      = "Index neu aufbauen",
        ["sc.zoomIn"]       = "Vergrößern",
        ["sc.zoomOut"]      = "Verkleinern",
        ["sc.zoomReset"]    = "Zoom zurücksetzen",
        ["sc.searchBox"]    = "Suchfeld",
        ["sc.pinnedSearch"] = "Angeheftete Suche starten (1–9 von oben)",
        ["sc.focusSearch"]  = "Zum Suchfeld",
        ["sc.clearSearch"]  = "Suche leeren (nochmal: in den Infobereich)",
        ["sc.gotoResults"]  = "Zur Ergebnisliste",
        ["sc.resultList"]   = "Ergebnisliste",
        ["sc.openItem"]     = "Auswahl öffnen",
        ["sc.viewProps"]    = "Eigenschaften anzeigen",
        ["sc.copyPath"]     = "Vollständigen Pfad kopieren",
        ["sc.copyName"]     = "Nur Dateinamen kopieren",
        ["sc.copyFile"]     = "Datei kopieren",
        ["sc.upFirst"]      = "↑ (erste Zeile)",
        ["sc.backToSearch"] = "Zurück zum Suchfeld",
        ["sc.dblClick"]     = "Doppelklick",
        ["sc.globalSc"]     = "Globales Kürzel",
        ["sc.toggleApp"]    = "Fenster von überall holen (Esc blendet aus)",
        ["sc.globalHotkey"] = "Fenster nach vorn holen (aus jeder App)",
        ["sc.searchTips"]   = "Suchtipps",
        ["sc.tip1"]         = "Mehrere Wörter mit Leerzeichen trennen — es werden nur Treffer mit allen Wörtern gefunden (UND-Suche).",
        ["sc.tip2"]         = "Die schnelle N-Gramm-Suche greift ab 2 koreanischen bzw. 3 lateinischen Zeichen.",

        ["help.search.title"] = "Suchhilfe",
        ["help.search.body"] =
            "#Einfache Suche\n" +
            "urlaub foto|Namen, die beide Wörter enthalten (Leerzeichen = UND)\n" +
            "*.jpg|nur jpg-Dateien (mehrere: *.jpg *.png)\n" +
            "\n" +
            "#Ausschließen\n" +
            "bericht -entwurf|Namen mit 'entwurf' überspringen\n" +
            "*.txt -*.log|txt suchen, log ausschließen\n" +
            "\n" +
            "#Nach Namensform (Platzhalter)\n" +
            "IMG_*_bearbeitet.txt|beginnt mit IMG_, endet auf _bearbeitet.txt (mit Endung)\n" +
            "IMG_*_bearbeitet*|für beliebige Endung ein * anhängen\n" +
            "foto_?.jpg|? ist genau ein Zeichen (foto_1 ja, foto_12 nein)\n" +
            "\n" +
            "#Nach Größe oder Datum\n" +
            "*.mp4 >500MB|mp4 größer als 500MB (Einheit nötig: KB MB GB TB)\n" +
            ">1GB|Dateien über 1GB (auch allein nutzbar)\n" +
            "foto >2026-01|geändert ab Januar 2026 (Reihenfolge Jahr-Monat-Tag)\n" +
            "*.pdf <2024|pdf, geändert vor 2024\n" +
            "bericht >week|letzte 7 Tage (today · week · month · year)\n" +
            "projekt folder:|nur Ordner (nur Dateien mit file:)\n" +
            "\n" +
            "#Nur in einem Ordner\n" +
            "foto D:\\Sicherung\\|nur unterhalb dieses Ordners suchen\n" +
            "foto \\Reisen\\|nur Einträge, deren Pfad den Ordner 'Reisen' enthält\n" +
            "foto \"D:\\Eigene Dateien\\\"|Pfade mit Leerzeichen in \"Anführungszeichen\"\n" +
            "\n" +
            "Alternativ: Rechtsklick auf ein Ergebnis → 'Nur in diesem Ordner suchen'.\n" +
            "Ein Pfad allein findet nichts — mit Wort oder Endung kombinieren.",

        ["about.title"]   = "Über",
        ["about.version"] = "Version {0}",
        ["about.desc"]    = "Ein Werkzeug, das das NTFS-Dateisystem direkt liest und dadurch sehr schnell sucht.",

        ["refresh.confirm.msg"]   = "Der Index wird komplett neu aufgebaut.\n\nDie Suche pausiert dabei kurz. Fortfahren?",
        ["refresh.confirm.title"] = "Index neu aufbauen",
        ["refresh.fail"]          = "Index-Neuaufbau fehlgeschlagen",

        ["common.error"]  = "Fehler",
        ["common.ok"]     = "OK",
        ["common.cancel"] = "Abbrechen",

        ["rd.report.title"]     = "Nicht gelöschte Einträge",
        ["rd.perm.title"]       = "Endgültig löschen",
        ["rd.perm.msg"]         = "{0} Eintrag/Einträge können nicht in den Papierkorb verschoben werden.\n\nEndgültig löschen?\nDies kann nicht rückgängig gemacht werden.",
        ["rd.blockedAt"]        = "Blockiert bei:",
        ["rd.lock.unknown"]     = "Programm konnte nicht ermittelt werden (evtl. System oder Antivirus)",
        ["rd.cause.locked"]     = "Von einem anderen Programm verwendet",
        ["rd.cause.denied"]     = "Zugriff verweigert",
        ["rd.cause.badname"]    = "Ungültiger Name oder Pfad",
        ["rd.cause.notempty"]   = "Ordner ist nicht leer",
        ["rd.cause.notfound"]   = "Bereits nicht mehr vorhanden",
        ["rd.cause.protected"]  = "Geschützter Systempfad",
        ["rd.cause.other"]      = "Anderer Fehler",
        ["rd.hint.locked"]      = "Schließen Sie die oben genannten Programme und versuchen Sie es erneut.",
        ["rd.hint.denied"]      = "Der Besitz liegt möglicherweise bei einem anderen Konto (z. B. TrustedInstaller).",
        ["rd.hint.badname"]     = "Der Explorer kann diesen Namen nicht verarbeiten. Erlauben Sie das endgültige Löschen.",
        ["rd.hint.notempty"]    = "Einige enthaltene Einträge wurden nicht entfernt. Siehe die anderen Ursachen oben.",
        ["rd.hint.protected"]   = "Aus Sicherheitsgründen nicht endgültig gelöscht. Entfernen Sie es bei Bedarf im Explorer.",
        ["rd.hint.notfound"]    = "Der Eintrag ist bereits verschwunden.",
        ["rd.hint.other"]       = "Die Ursache konnte nicht ermittelt werden.",
        ["dn.permOne"]          = "Endgültig gelöscht",
        ["dn.permMulti"]        = "{0} Eintrag/Einträge endgültig gelöscht.",
        ["dn.mixed"]            = "{0} gelöscht ({1} endgültig)",
        ["dn.permSuffix"]       = "{0} endgültig",
    };
}
