namespace HdrTracer.Core;

// 스페인어 문자열. 번역이 빠진 키는 자동으로 영어로 대체된다.
public static partial class Localization
{
    internal static readonly Dictionary<string, string> _es = new()
    {
        ["menu.settings"]     = "Configuración",
        ["menu.refresh"]      = "Actualizar índice",
        ["menu.resetCols"]    = "Restablecer ancho de columnas",
        ["menu.zoom"]         = "Zoom",
        ["menu.zoom.in"]      = "Acercar",
        ["menu.zoom.out"]     = "Alejar",
        ["menu.zoom.reset"]   = "Restablecer zoom",
        ["menu.shortcuts"]    = "Atajos",
        ["menu.about"]        = "Acerca de",
        ["menu.language"]     = "Idioma",
        ["menu.lang.ko"]      = "Coreano",
        ["menu.lang.en"]      = "Inglés",
        ["menu.lang.zh"]      = "Chino (simplificado)",
        ["menu.lang.ja"]      = "Japonés",
        ["menu.lang.es"]      = "Español",
        ["menu.lang.de"]      = "Alemán",
        ["menu.lang.fr"]      = "Francés",
        ["menu.searchHelp"]   = "Ayuda de búsqueda",
        ["menu.export"]       = "Exportar resultados (CSV)",
        ["ctx.export"]        = "Exportar elementos seleccionados",
        ["export.done"]       = "Se guardaron {0} elementos en CSV.",
        ["menu.filter"]       = "Filtrar",

        ["tip.menu"]     = "Menú",
        ["tip.minimize"] = "Minimizar",
        ["tip.maximize"] = "Maximizar",
        ["tip.close"]    = "Cerrar",
        ["tip.search"]   = "Buscar",
        ["tip.delete"]   = "Eliminar",
        ["tip.pin"]      = "Fijar / Desfijar",

        ["search.placeholder"] = "Escribe un nombre de archivo y pulsa Enter",
        ["empty.title"] = "Sin resultados",
        ["empty.body"]  = "Prueba con un término más corto o distinto\nVarias palabras solo coinciden con elementos que las contienen todas\nAyuda: menú (HdrTracer ▼) → Ayuda de búsqueda",

        ["ctx.open"]           = "Abrir",
        ["ctx.runAsAdmin"]     = "Ejecutar como administrador",
        ["ctx.openWith"]       = "Abrir con...",
        ["ctx.reveal"]         = "Mostrar en la carpeta",
        ["ctx.searchFolder"]   = "Buscar solo en esta carpeta",
        ["ctx.copyPath"]       = "Copiar ruta",
        ["ctx.copyName"]       = "Copiar nombre",
        ["ctx.rename"]         = "Cambiar nombre",
        ["ctx.copyFile"]       = "Copiar archivo",
        ["ctx.delete"]         = "Mover a la papelera",
        ["ctx.properties"]     = "Propiedades",
        ["ctx.rename.title"]   = "Cambiar nombre",
        ["ctx.rename.prompt"]  = "Nuevo nombre:",
        ["ctx.rename.extWarn"] = "Cambiar la extensión puede hacer que el archivo no se pueda usar.\n¿Continuar de todos modos?",
        ["ctx.delete.confirm"] = "¿Mover este archivo a la papelera?",
        ["ctx.delete.more"]    = "…y {0} más",
        ["ctx.delete.danger"]  = "⚠️ Advertencia: se incluyen elementos críticos del sistema. Eliminarlos puede desestabilizar el sistema.",
        ["ctx.delete.confirm.multi"] = "¿Mover los {0} elementos seleccionados a la papelera?",
        ["ctx.delete.done.multi"]    = "Se movieron {0} elementos a la papelera.",
        ["ctx.delete.partial"] = "{0} correctos, {1} fallidos",
        ["ctx.copyFile.multi"] = "{0} archivos copiados al portapapeles",
        ["ctx.copyFile.none"]  = "No hay archivos para copiar",
        ["ctx.copyPath.multi"] = "{0} rutas copiadas",
        ["ctx.copyName.multi"] = "{0} nombres copiados",
        ["ctx.open.multi"]     = "{0} elementos abiertos",
        ["ctx.open.partial"]   = "{0} abiertos, {1} fallidos",
        ["ctx.delete.title"]   = "Mover a la papelera",
        ["ctx.error"]          = "Error",
        ["ctx.notExecutable"]  = "No es un archivo ejecutable",

        ["filter.doc"]         = "Documentos",
        ["filter.img"]         = "Imágenes",
        ["filter.media"]       = "Multimedia",
        ["filter.exe"]         = "Aplicaciones",
        ["filter.zip"]         = "Comprimidos",
        ["filter.size"]        = "Tamaño",
        ["filter.size.10mb"]   = "Más de 10MB",
        ["filter.size.100mb"]  = "Más de 100MB",
        ["filter.size.1gb"]    = "Más de 1GB",
        ["filter.date"]        = "Fecha",
        ["filter.date.today"]  = "Hoy",
        ["filter.date.week"]   = "Última semana",
        ["filter.date.month"]  = "Último mes",
        ["filter.date.year"]   = "Último año",
        ["filter.clear"]       = "Quitar condición",
        ["filter.kind"]        = "Tipo",
        ["filter.kind.folder"] = "Solo carpetas",
        ["filter.kind.file"]   = "Solo archivos",

        ["col.drive"]  = "UNI",
        ["col.name"]   = "Nombre",
        ["col.path"]   = "Ruta",
        ["col.size"]   = "Tamaño",
        ["col.date"]   = "Modificado",
        ["status.indexing"]         = "Indexando unidades...",
        ["status.total"]            = "Total",
        ["status.items"]            = " elementos",
        ["status.results"]          = "resultados",
        ["status.refreshDone"]      = "Índice actualizado",
        ["status.selected"]         = "{0} seleccionados, {1}",
        ["status.selectedMany"]     = "{0:N0} seleccionados",
        ["status.indexingProgress"] = "Indexando… {0} s transcurridos  ·  {1}",
        ["status.driveDone"]        = "{0} listo ({1:N0})",
        ["status.driveWorking"]     = "{0} en curso",

        ["tray.open"]         = "Abrir HdrTracer",
        ["tray.exit"]         = "Salir",
        ["tray.pinned"]       = "Búsquedas fijadas",
        ["tray.pinned.empty"] = "No hay búsquedas fijadas",
        ["tray.settings"]     = "Configuración",

        ["settings.title"]     = "Configuración",
        ["settings.indexing"]  = "Indexación",
        ["settings.usb"]       = "Indexar unidades extraíbles",
        ["settings.usb.desc"]  = "Incluye en la búsqueda los archivos de unidades USB.",
        ["settings.tray"]      = "Minimizar a la bandeja al cerrar",
        ["settings.tray.desc"] = "El botón X oculta en la bandeja en lugar de salir. Haz clic derecho en el icono para salir.",
        ["settings.hidden"]      = "Mostrar elementos ocultos y del sistema",
        ["settings.hidden.desc"] = "Incluye en los resultados los elementos ocultos+sistema (p. ej., carpetas de protección del antivirus).",
        ["settings.ok"]        = "Aceptar",
        ["settings.usbOn"]     = "Las unidades USB se indexarán al pulsar Aceptar.",
        ["settings.usbOff"]    = "Los datos USB indexados se liberarán de la memoria al pulsar Aceptar.",
        ["settings.autostart"]      = "Iniciar con Windows",
        ["settings.autostart.desc"] = "Se ejecuta en la bandeja al iniciar sesión (Programador de tareas, sin aviso de UAC).",
        ["settings.autostart.fail"] = "No se pudo cambiar el inicio automático.",
        ["settings.excluded"]      = "Nombres de carpeta excluidos de la búsqueda",
        ["settings.excluded.desc"] = "Sepáralos con punto y coma (;). Las carpetas con estos nombres y todo su contenido se ocultan de los resultados. Ej.: WinSxS; node_modules",
        ["settings.hotkey"]        = "Atajo global (Win+Alt+S)",
        ["settings.hotkey.desc"]   = "Abre la ventana desde cualquier app con Win+Alt+S. Pulsa Esc para ocultarla.",
        ["settings.restoreSearch"]      = "Restaurar la última búsqueda al iniciar",
        ["settings.restoreSearch.desc"] = "Al abrir la app de nuevo, se muestra ya el resultado de tu última búsqueda.",
        ["hotkey.fail"]            = "No se pudo registrar el atajo global (Win+Alt+S): puede que otro programa lo esté usando.",

        ["sc.title"]        = "Atajos",
        ["sc.appMenu"]      = "Menú de la app",
        ["sc.openSettings"] = "Abrir configuración",
        ["sc.refresh"]      = "Actualizar índice",
        ["sc.zoomIn"]       = "Acercar",
        ["sc.zoomOut"]      = "Alejar",
        ["sc.zoomReset"]    = "Restablecer zoom",
        ["sc.searchBox"]    = "Cuadro de búsqueda",
        ["sc.pinnedSearch"] = "Ejecutar búsqueda fijada (1–9 desde arriba)",
        ["sc.focusSearch"]  = "Ir al cuadro de búsqueda",
        ["sc.clearSearch"]  = "Borrar la búsqueda (de nuevo: ocultar en la bandeja)",
        ["sc.gotoResults"]  = "Ir a la lista de resultados",
        ["sc.resultList"]   = "Lista de resultados",
        ["sc.openItem"]     = "Abrir el elemento seleccionado",
        ["sc.viewProps"]    = "Ver propiedades",
        ["sc.copyPath"]     = "Copiar la ruta completa",
        ["sc.copyName"]     = "Copiar solo el nombre",
        ["sc.copyFile"]     = "Copiar archivo",
        ["sc.upFirst"]      = "↑ (primera fila)",
        ["sc.backToSearch"] = "Volver al cuadro de búsqueda",
        ["sc.dblClick"]     = "Doble clic",
        ["sc.globalSc"]     = "Atajo global",
        ["sc.toggleApp"]    = "Abrir la ventana desde cualquier sitio (Esc la oculta)",
        ["sc.globalHotkey"] = "Traer la ventana al frente (desde cualquier app)",
        ["sc.searchTips"]   = "Consejos de búsqueda",
        ["sc.tip1"]         = "Separa varias palabras con espacios para encontrar resultados que las contengan todas (búsqueda Y).",
        ["sc.tip2"]         = "La búsqueda rápida N-gram funciona con 2+ caracteres coreanos o 3+ caracteres latinos.",

        ["help.search.title"] = "Ayuda de búsqueda",
        ["help.search.body"] =
            "#Búsqueda básica\n" +
            "vacaciones foto|nombres que contienen ambas palabras (espacio = Y)\n" +
            "*.jpg|solo archivos jpg (varios: *.jpg *.png)\n" +
            "\n" +
            "#Excluir\n" +
            "informe -borrador|omite los nombres que contienen 'borrador'\n" +
            "*.txt -*.log|busca txt pero no log\n" +
            "\n" +
            "#Por forma del nombre (comodines)\n" +
            "IMG_*_editada.txt|empieza por IMG_ y acaba en _editada.txt (con extensión)\n" +
            "IMG_*_editada*|añade * al final para cualquier extensión\n" +
            "foto_?.jpg|? es un solo carácter (foto_1 sí, foto_12 no)\n" +
            "\n" +
            "#Por tamaño o fecha\n" +
            "*.mp4 >500MB|mp4 de más de 500MB (unidad obligatoria: KB MB GB TB)\n" +
            ">1GB|archivos de más de 1GB (se puede usar solo)\n" +
            "foto >2026-01|modificados desde enero de 2026 (orden año-mes-día)\n" +
            "*.pdf <2024|pdf modificados antes de 2024\n" +
            "informe >week|últimos 7 días (today · week · month · year)\n" +
            "proyecto folder:|solo carpetas (usa file: para solo archivos)\n" +
            "\n" +
            "#Solo dentro de una carpeta\n" +
            "foto D:\\Copias\\|busca solo bajo esa carpeta\n" +
            "foto \\Viajes\\|solo elementos cuya ruta tiene la carpeta 'Viajes'\n" +
            "foto \"D:\\Mis documentos\\\"|rutas con espacios entre \"comillas\"\n" +
            "\n" +
            "También puedes hacer clic derecho en un resultado → 'Buscar solo en esta carpeta'.\n" +
            "Una ruta sola no busca nada: combínala con una palabra o una extensión.",

        ["about.title"]   = "Acerca de",
        ["about.version"] = "Versión {0}",
        ["about.desc"]    = "Una herramienta que lee directamente el sistema de archivos NTFS para buscar rápido.",

        ["refresh.confirm.msg"]   = "El índice se reconstruirá desde cero.\n\nLa búsqueda puede pausarse un momento. ¿Continuar?",
        ["refresh.confirm.title"] = "Actualizar índice",
        ["refresh.fail"]          = "Error al actualizar el índice",

        ["common.error"]  = "Error",
        ["common.ok"]     = "Aceptar",
        ["common.cancel"] = "Cancelar",
    };
}
