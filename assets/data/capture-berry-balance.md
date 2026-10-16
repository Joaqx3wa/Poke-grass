# Balance de bayas de captura

Los seis iconos proceden de `assets/items/berries/` y se usan en RGBA a 32 × 32 píxeles. La UI conserva un ID de inventario independiente para cada variante.

| Baya | Multiplicador | Efecto |
|---|---:|---|
| Frambu | captura ×1,5 | Aumenta la probabilidad de captura. |
| Frambu Plateada | captura ×1,9 | Mejora más que Frambu. |
| Frambu Dorada | captura ×2,5 | La mayor mejora; los intentos con Ball normal siguen limitados a 99 %. |
| Latano | huida ×0,35 | Reduce la probabilidad de huida tras un fallo. |
| Latano Plateada | huida ×0,20 | Protege más que Latano. |
| Latano Dorada | huida ×0,10 | Máxima protección; no anula la probabilidad de huida. |

Los multiplicadores se centralizan en `CaptureMechanics.BerryEffectMultipliers`. Solo se puede aplicar una baya por encuentro y el efecto se conserva hasta que este termina. Las variantes plateadas y doradas comienzan con cantidad cero; el kit inicial solo entrega tres Frambu y tres Latano normales. Al migrar guardados antiguos, los IDs nuevos se inicializan a cero y no se vuelve a conceder el kit.
