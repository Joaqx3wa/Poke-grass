# Balance inicial de recompensas

Las cantidades y probabilidades están centralizadas en `reward-balance.json`. El archivo usa IDs de inventario existentes y rutas a los iconos originales en `assets/items/`.

## Suministros periódicos

Se entrega un paquete cada 2.400 segundos de aplicación abierta. Cada paquete garantiza 4–7 Poké Balls y 2–4 Bayas Frambu; además hace dos selecciones ponderadas. La suma de pesos de la tabla es 131: Balls y bayas comunes ocupan 97/131 del peso, repelentes 12/131 e inciensos 22/131. No hay Master Ball en esa tabla. El reloj se guarda localmente al menos cada 10 segundos y al cerrar; el tiempo cerrado no se descuenta.

## Regalo diario

Una entrega por fecha local, identificada de forma idempotente por `daily:AAAA-MM-DD`. Garantiza 8–12 Poké Balls, 3–5 Frambu y 2–4 Latano, con dos selecciones ponderadas adicionales. Es más generosa que el paquete periódico y se concede al iniciar si ese día aún no se entregó.

## Duplicados normales

Un duplicado normal se determina por especie y forma, se registra en el historial sin ocupar otro espacio de caja y recibe la tabla de su rareza actual. Común da 2–4 Balls y una probabilidad del 70 % de 1–2 Frambu; las categorías siguientes incrementan cantidades/calidad y pueden incluir objetos más raros. La tabla no altera la probabilidad de encuentro ni las probabilidades de captura.

## Duplicados shiny

Cada ejemplar shiny siempre se almacena de forma independiente. Solo un shiny duplicado de una especie y forma que ya estaba registrada como shiny activa dos selecciones sorpresa. La Master Ball se evalúa aparte con probabilidad 1/2.048 por shiny duplicado y no aparece en ningún otro paquete o tabla.

## Persistencia

Cada recompensa y su marca de evento se escriben en la misma transacción del guardado local. Los eventos de captura usan IDs únicos; las entregas diarias usan la fecha y los suministros usan IDs únicos. Los cambios no vuelven a otorgar el kit inicial a partidas existentes.
