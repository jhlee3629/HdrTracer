namespace HdrTracer.Core;

// 프랑스어 문자열. 번역이 빠진 키는 자동으로 영어로 대체된다.
public static partial class Localization
{
    internal static readonly Dictionary<string, string> _fr = new()
    {
        ["menu.settings"]     = "Paramètres",
        ["menu.refresh"]      = "Reconstruire l'index",
        ["menu.resetCols"]    = "Réinitialiser les colonnes",
        ["menu.zoom"]         = "Zoom",
        ["menu.zoom.in"]      = "Agrandir",
        ["menu.zoom.out"]     = "Réduire",
        ["menu.zoom.reset"]   = "Réinitialiser le zoom",
        ["menu.shortcuts"]    = "Raccourcis",
        ["menu.about"]        = "À propos",
        ["menu.language"]     = "Langue",
        ["menu.lang.ko"]      = "Coréen",
        ["menu.lang.en"]      = "Anglais",
        ["menu.lang.zh"]      = "Chinois (simplifié)",
        ["menu.lang.ja"]      = "Japonais",
        ["menu.lang.es"]      = "Espagnol",
        ["menu.lang.de"]      = "Allemand",
        ["menu.lang.fr"]      = "Français",
        ["menu.searchHelp"]   = "Aide à la recherche",
        ["menu.export"]       = "Exporter les résultats (CSV)",
        ["ctx.export"]        = "Exporter la sélection",
        ["export.done"]       = "{0} éléments enregistrés en CSV.",
        ["menu.filter"]       = "Filtrer",

        ["tip.menu"]     = "Menu",
        ["tip.minimize"] = "Réduire",
        ["tip.maximize"] = "Agrandir",
        ["tip.close"]    = "Fermer",
        ["tip.search"]   = "Rechercher",
        ["tip.delete"]   = "Supprimer",
        ["tip.pin"]      = "Épingler / Détacher",

        ["search.placeholder"] = "Saisissez un nom de fichier puis Entrée",
        ["empty.title"] = "Aucun résultat",
        ["empty.body"]  = "Essayez un terme plus court ou différent\nPlusieurs mots ne correspondent qu'aux éléments qui les contiennent tous\nAide : menu (HdrTracer ▼) → Aide à la recherche",

        ["ctx.open"]           = "Ouvrir",
        ["ctx.runAsAdmin"]     = "Exécuter en tant qu'administrateur",
        ["ctx.openWith"]       = "Ouvrir avec...",
        ["ctx.reveal"]         = "Afficher dans le dossier",
        ["ctx.searchFolder"]   = "Rechercher dans ce dossier",
        ["ctx.copyPath"]       = "Copier le chemin",
        ["ctx.copyName"]       = "Copier le nom",
        ["ctx.rename"]         = "Renommer",
        ["ctx.copyFile"]       = "Copier le fichier",
        ["ctx.delete"]         = "Mettre à la corbeille",
        ["ctx.properties"]     = "Propriétés",
        ["ctx.rename.title"]   = "Renommer",
        ["ctx.rename.prompt"]  = "Nouveau nom :",
        ["ctx.rename.extWarn"] = "Modifier l'extension peut rendre le fichier inutilisable.\nContinuer quand même ?",
        ["ctx.delete.confirm"] = "Mettre ce fichier à la corbeille ?",
        ["ctx.delete.more"]    = "…et {0} autres",
        ["ctx.delete.danger"]  = "⚠️ Attention : des éléments critiques du système sont inclus. Les supprimer peut rendre le système instable.",
        ["ctx.delete.confirm.multi"] = "Mettre les {0} éléments sélectionnés à la corbeille ?",
        ["ctx.delete.done.multi"]    = "{0} éléments mis à la corbeille.",
        ["ctx.delete.partial"] = "{0} réussis, {1} échoués",
        ["ctx.copyFile.multi"] = "{0} fichiers copiés dans le presse-papiers",
        ["ctx.copyFile.none"]  = "Aucun fichier à copier",
        ["ctx.copyPath.multi"] = "{0} chemins copiés",
        ["ctx.copyName.multi"] = "{0} noms copiés",
        ["ctx.open.multi"]     = "{0} éléments ouverts",
        ["ctx.open.partial"]   = "{0} ouverts, {1} échoués",
        ["ctx.delete.title"]   = "Mettre à la corbeille",
        ["ctx.error"]          = "Erreur",
        ["ctx.notExecutable"]  = "Ce n'est pas un exécutable",

        ["filter.doc"]         = "Documents",
        ["filter.img"]         = "Images",
        ["filter.media"]       = "Médias",
        ["filter.exe"]         = "Applications",
        ["filter.zip"]         = "Archives",
        ["filter.size"]        = "Taille",
        ["filter.size.10mb"]   = "Plus de 10MB",
        ["filter.size.100mb"]  = "Plus de 100MB",
        ["filter.size.1gb"]    = "Plus de 1GB",
        ["filter.date"]        = "Date",
        ["filter.date.today"]  = "Aujourd'hui",
        ["filter.date.week"]   = "7 derniers jours",
        ["filter.date.month"]  = "30 derniers jours",
        ["filter.date.year"]   = "Dernière année",
        ["filter.clear"]       = "Effacer le filtre",
        ["filter.kind"]        = "Type",
        ["filter.kind.folder"] = "Dossiers seulement",
        ["filter.kind.file"]   = "Fichiers seulement",

        ["col.drive"]  = "DSQ",
        ["col.name"]   = "Nom",
        ["col.path"]   = "Chemin",
        ["col.size"]   = "Taille",
        ["col.date"]   = "Modifié le",
        ["status.indexing"]         = "Indexation des lecteurs...",
        ["status.total"]            = "Total",
        ["status.items"]            = " éléments",
        ["status.results"]          = "résultats",
        ["status.refreshDone"]      = "Index reconstruit",
        ["status.selected"]         = "{0} sélectionnés, {1}",
        ["status.selectedMany"]     = "{0:N0} sélectionnés",
        ["status.indexingProgress"] = "Indexation… {0} s écoulées  ·  {1}",
        ["status.driveDone"]        = "{0} terminé ({1:N0})",
        ["status.driveWorking"]     = "{0} en cours",

        ["tray.open"]         = "Ouvrir HdrTracer",
        ["tray.exit"]         = "Quitter",
        ["tray.pinned"]       = "Recherches épinglées",
        ["tray.pinned.empty"] = "Aucune recherche épinglée",
        ["tray.settings"]     = "Paramètres",

        ["settings.title"]     = "Paramètres",
        ["settings.indexing"]  = "Indexation",
        ["settings.usb"]       = "Indexer les lecteurs amovibles",
        ["settings.usb.desc"]  = "Inclure dans la recherche les fichiers des clés USB.",
        ["settings.tray"]      = "Réduire dans la zone de notification",
        ["settings.tray.desc"] = "Le bouton X masque dans la zone de notification au lieu de quitter. Clic droit sur l'icône pour quitter.",
        ["settings.hidden"]      = "Afficher les éléments cachés et système",
        ["settings.hidden.desc"] = "Inclure dans les résultats les éléments cachés+système (par ex. dossiers de protection d'antivirus).",
        ["settings.ok"]        = "OK",
        ["settings.usbOn"]     = "Les lecteurs USB seront indexés après validation.",
        ["settings.usbOff"]    = "Les données USB indexées seront libérées de la mémoire après validation.",
        ["settings.autostart"]      = "Démarrer avec Windows",
        ["settings.autostart.desc"] = "Se lance automatiquement dans la zone de notification à l'ouverture de session (Planificateur de tâches, sans UAC).",
        ["settings.autostart.fail"] = "Impossible de modifier le démarrage automatique.",
        ["settings.excluded"]      = "Noms de dossiers exclus de la recherche",
        ["settings.excluded.desc"] = "Séparez-les par des points-virgules (;). Les dossiers portant ces noms et tout leur contenu sont masqués. Ex. : WinSxS; node_modules",
        ["settings.hotkey"]        = "Raccourci global (Win+Alt+S)",
        ["settings.hotkey.desc"]   = "Appelez la fenêtre depuis n'importe quelle app avec Win+Alt+S. Esc la masque.",
        ["settings.restoreSearch"]      = "Restaurer la dernière recherche au démarrage",
        ["settings.restoreSearch.desc"] = "À la réouverture, la dernière recherche est déjà exécutée.",
        ["hotkey.fail"]            = "Échec de l'enregistrement du raccourci global (Win+Alt+S) — un autre programme l'utilise peut-être.",

        ["sc.title"]        = "Raccourcis",
        ["sc.appMenu"]      = "Menu de l'application",
        ["sc.openSettings"] = "Ouvrir les paramètres",
        ["sc.refresh"]      = "Reconstruire l'index",
        ["sc.zoomIn"]       = "Agrandir",
        ["sc.zoomOut"]      = "Réduire",
        ["sc.zoomReset"]    = "Réinitialiser le zoom",
        ["sc.searchBox"]    = "Champ de recherche",
        ["sc.pinnedSearch"] = "Lancer une recherche épinglée (1–9 depuis le haut)",
        ["sc.focusSearch"]  = "Aller au champ de recherche",
        ["sc.clearSearch"]  = "Effacer la recherche (à nouveau : masquer)",
        ["sc.gotoResults"]  = "Aller à la liste des résultats",
        ["sc.resultList"]   = "Liste des résultats",
        ["sc.openItem"]     = "Ouvrir l'élément sélectionné",
        ["sc.viewProps"]    = "Afficher les propriétés",
        ["sc.copyPath"]     = "Copier le chemin complet",
        ["sc.copyName"]     = "Copier seulement le nom",
        ["sc.copyFile"]     = "Copier le fichier",
        ["sc.upFirst"]      = "↑ (1re ligne)",
        ["sc.backToSearch"] = "Retour au champ de recherche",
        ["sc.dblClick"]     = "Double-clic",
        ["sc.globalSc"]     = "Raccourci global",
        ["sc.toggleApp"]    = "Appeler la fenêtre depuis n'importe où (Esc la masque)",
        ["sc.globalHotkey"] = "Mettre la fenêtre au premier plan (depuis toute app)",
        ["sc.searchTips"]   = "Astuces de recherche",
        ["sc.tip1"]         = "Séparez plusieurs mots par des espaces pour trouver les résultats qui les contiennent tous (recherche ET).",
        ["sc.tip2"]         = "La recherche rapide N-gramme fonctionne dès 2 caractères coréens ou 3 caractères latins.",

        ["help.search.title"] = "Aide à la recherche",
        ["help.search.body"] =
            "#Recherche de base\n" +
            "vacances photo|noms contenant les deux mots (espace = ET)\n" +
            "*.jpg|fichiers jpg seulement (plusieurs : *.jpg *.png)\n" +
            "\n" +
            "#Exclure\n" +
            "rapport -brouillon|ignore les noms contenant 'brouillon'\n" +
            "*.txt -*.log|cherche txt mais pas log\n" +
            "\n" +
            "#Par forme du nom (jokers)\n" +
            "IMG_*_retouche.txt|commence par IMG_ et finit par _retouche.txt (extension comprise)\n" +
            "IMG_*_retouche*|ajoutez * à la fin pour n'importe quelle extension\n" +
            "photo_?.jpg|? correspond à un seul caractère (photo_1 oui, photo_12 non)\n" +
            "\n" +
            "#Par taille ou date\n" +
            "*.mp4 >500MB|mp4 de plus de 500MB (unité obligatoire : KB MB GB TB)\n" +
            ">1GB|fichiers de plus de 1GB (utilisable seul)\n" +
            "photo >2026-01|modifiés depuis janvier 2026 (ordre année-mois-jour)\n" +
            "*.pdf <2024|pdf modifiés avant 2024\n" +
            "rapport >week|7 derniers jours (today · week · month · year)\n" +
            "projet folder:|dossiers seulement (file: pour les fichiers)\n" +
            "\n" +
            "#Dans un dossier précis\n" +
            "photo D:\\Sauvegarde\\|chercher uniquement sous ce dossier\n" +
            "photo \\Voyages\\|éléments dont le chemin contient le dossier 'Voyages'\n" +
            "photo \"D:\\Mes documents\\\"|chemins avec espaces entre \"guillemets\"\n" +
            "\n" +
            "Vous pouvez aussi faire un clic droit sur un résultat → « Rechercher dans ce dossier ».\n" +
            "Un chemin seul ne cherche rien : combinez-le avec un mot ou une extension.",

        ["about.title"]   = "À propos",
        ["about.version"] = "Version {0}",
        ["about.desc"]    = "Un outil qui lit directement le système de fichiers NTFS pour une recherche très rapide.",

        ["refresh.confirm.msg"]   = "L'index va être reconstruit entièrement.\n\nLa recherche peut s'interrompre brièvement. Continuer ?",
        ["refresh.confirm.title"] = "Reconstruire l'index",
        ["refresh.fail"]          = "Échec de la reconstruction de l'index",

        ["common.error"]  = "Erreur",
        ["common.ok"]     = "OK",
        ["common.cancel"] = "Annuler",
    };
}
