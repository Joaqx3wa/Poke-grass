# Informe de tamaño de PokéGrass

Medición local del 16 de octubre de 2026. MiB usa 1.048.576 bytes.

| Elemento | Tamaño |
|---|---:|
| Árbol de fuentes y recursos, sin `.git`, compilaciones ni paquetes generados | 101,62 MiB; 10.534 archivos |
| Recursos originales en `assets/` | 100,48 MiB |
| Publicación self-contained Windows x64 en `publish/` | 273,04 MiB; 10.945 archivos |
| Instalador Velopack `Releases/PokeGrass-win-Setup.exe` (0.1.0-beta) | 177,38 MiB |
| Paquete completo `PokeGrass-0.1.0-beta-full.nupkg` | 170,21 MiB |
| Paquete portable ZIP | 170,21 MiB |

## Carpetas de recursos más grandes

| Carpeta | Tamaño |
|---|---:|
| `assets/ui/` | 36,07 MiB |
| `assets/pokemon/` | 26,11 MiB |
| `assets/reference/` | 19,11 MiB |
| `assets/sounds/` | 17,97 MiB |
| `assets/data/` | 1,12 MiB |

Los recursos se copian completos a la publicación porque el programa compone rutas de sprites y sonidos dinámicamente. No se borraron ni comprimieron archivos. `assets/reference/` es la oportunidad más clara para reducir el instalador (hasta unos 19 MiB) si una revisión posterior confirma que ninguna función ni vista de diagnóstico lo carga en tiempo de ejecución.

## Alcance de la medición

El árbol de fuentes cuenta código, workflows, documentos y todos los recursos originales; excluye `.git`, `bin`, `obj`, `.vs`, `artifacts`, `outputs`, `work`, `publish` y `Releases`. Las carpetas generadas de compilación ya existentes localmente no se borraron. El repositorio nuevo aún no tiene commits ni historial; el tamaño subido a GitHub se conocerá tras el primer push.

El instalador se generó localmente con Velopack 1.2.161 y runtime `win-x64`. No tiene firma de código, por lo que Windows puede mostrar el aviso habitual de editor desconocido. No se ejecutó una instalación limpia ni una actualización entre dos Releases; ambas requieren publicar los paquetes y probar en una máquina Windows limpia.
