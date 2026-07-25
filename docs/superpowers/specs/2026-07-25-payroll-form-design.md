# Formulario web para generar guías de pago (PayRoll)

## Contexto

`SwedenBttnBit` es hoy un proyecto Web API puro (`Program.cs` solo llama a
`AddControllers`, sin vistas ni archivos estáticos). El único endpoint,
`POST /PayRoll/get-payroll`, recibe un JSON `PayRoll` y devuelve un PDF
generado con iText7. No hay UI: hoy se debe invocar el endpoint a mano
(Swagger/Postman) armando el JSON manualmente cada vez.

El mismo cliente (Ak Drilling International S.A.) se repite en casi todas las
guías, con la misma dirección/distrito/RUC, y hay un catálogo fijo de
productos que se vende con frecuencia. Lo único que cambia consistentemente
es la fecha (siempre hoy), el número de guía, y ajustes puntuales a
cantidades/productos. Además, es común generar más de un intento de PDF para
una misma guía antes de decidir cuál es la versión final que se entregó.

## Objetivo

Agregar un formulario web servido por el mismo proyecto que:
1. Prellena los campos que casi no cambian (cliente, fecha, productos
   frecuentes), dejándolos editables.
2. Sugiere el siguiente número de guía en base al historial.
3. Genera el PDF (reusando el endpoint existente) y lo abre en una pestaña
   nueva para revisión antes de descargar/imprimir.
4. Guarda automáticamente cada PDF generado (archivo + metadata) para poder
   ver versiones anteriores de una guía y marcar cuál fue la final.

## Fuera de alcance

- Envío de correo (el código existente para esto está comentado y no se
  toca).
- Autenticación / multi-usuario (sigue siendo una herramienta interna de un
  solo operador).
- Edición del catálogo de productos frecuentes desde la UI (se define una
  vez en el código a partir del JSON provisto).
- Migración a .NET 8 u otras mejoras de dependencias (tratado aparte).

## Arquitectura

- **Frontend**: página estática (`wwwroot/index.html` + JS plano, sin build
  step ni framework) servida por el mismo proyecto ASP.NET. Se habilita
  `app.UseDefaultFiles()` + `app.UseStaticFiles()` en `Program.cs` (hoy
  ausentes).
- **Persistencia de PDFs generados**: cada PDF se guarda en disco, agrupado
  por año/mes:
  `App_Data/Guides/{yyyy}/{MM}/{GuideNumber}_{ddMMyyyy}_{Destinatario}_{id}.pdf`
  El sufijo `{id}` evita colisiones entre múltiples intentos del mismo número
  de guía en el mismo día.
- **Historial (metadata)**: un archivo `App_Data/payroll-history.json` con
  una entrada por cada PDF generado. No se usa base de datos (EF Core/SQLite
  sería sobre-ingeniería para un solo usuario y bajo volumen); un archivo
  JSON con acceso serializado (lock en memoria) alcanza.

### Modelo de datos del historial

```csharp
public class PayRollHistoryEntry
{
    public string Id { get; set; }            // Guid o similar, único
    public string GuideNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsFinal { get; set; }
    public string PdfRelativePath { get; set; } // ruta bajo App_Data/Guides/...
    public PayRoll Payload { get; set; }         // el PayRoll completo enviado
}
```

## Endpoints

### `POST /PayRoll/get-payroll` (existente, comportamiento extendido)

Mismo contrato de entrada/salida que hoy (recibe `PayRoll`, devuelve el PDF
como archivo). Adicionalmente, en el mismo request:
1. Guarda el PDF generado en
   `App_Data/Guides/{yyyy}/{MM}/...` (año/mes según `payroll.Date`).
2. Crea una entrada en el historial con `IsFinal = false`.

Si el guardado en disco falla, no debe impedir que el PDF se devuelva al
usuario (el historial es una conveniencia, no el flujo crítico).

### `GET /PayRoll/history/{id}/pdf` (nuevo)

Sirve el PDF guardado para esa entrada del historial (`Content-Type:
application/pdf`, leído desde `PdfRelativePath`). `App_Data` no se expone
como contenido estático por defecto en ASP.NET Core (a diferencia de
`wwwroot`), así que este endpoint dedicado es lo que respalda el botón "Ver
PDF" del panel de historial, en vez de servir el archivo directamente como
estático. Devuelve 404 si el id o el archivo no existen.

### `GET /PayRoll/history` (nuevo)

Devuelve la lista de entradas del historial, más recientes primero. Incluye
todos los campos de `PayRollHistoryEntry` (incluyendo `Payload`, para poder
recargar el formulario con esos datos sin otra llamada).

### `POST /PayRoll/history/{id}/final` (nuevo)

Marca la entrada `{id}` como `IsFinal = true`, y desmarca cualquier otra
entrada con el mismo `GuideNumber` que estuviera marcada como final (solo
puede haber una versión final por número de guía). Devuelve 404 si el id no
existe.

## Flujo en el frontend

1. Al cargar la página, se hace `GET /PayRoll/history` para:
   - Pintar el panel de historial.
   - Calcular la sugerencia de siguiente número de guía:
     `max(GuideNumber de entradas con IsFinal = true, parseado como entero) + 1`.
     Si no hay entradas finales aún, o el número no es parseable como entero,
     el campo queda vacío para completarlo a mano.
2. El formulario se prellena con:
   - Cliente (Destinatario, Dirección, Distrito, RUC): valores fijos tomados
     del JSON de ejemplo provisto por el usuario.
   - Fecha: hoy.
   - N° de guía: la sugerencia calculada en el paso 1.
   - Productos: las 8 filas del JSON de ejemplo (descripción, cantidad,
     precio), como catálogo de productos frecuentes.
   Todos los campos son editables; los de productos también se pueden
   eliminar o agregar filas nuevas.
3. Al enviar ("Generar PDF"): `POST /PayRoll/get-payroll` con el JSON armado
   desde el formulario. La respuesta (PDF binario) se abre en una pestaña
   nueva vía Blob URL (no se descarga automáticamente). Luego se refresca el
   historial (`GET /PayRoll/history`) para mostrar la nueva entrada.
4. Panel de historial: lista plana ordenada por fecha de creación
   descendente. Cada fila muestra n° de guía, fecha, cliente, y un badge
   "Final" si corresponde. Acciones por fila:
   - **Ver PDF**: abre `GET /PayRoll/history/{id}/pdf` en una pestaña nueva.
   - **Marcar como final** (oculto si la fila ya es final): llama a
     `POST /PayRoll/history/{id}/final` y refresca la lista.
   - (Opcional, no bloqueante) click en la fila carga el `Payload` guardado
     de vuelta en el formulario, para ver/editar esa versión.

## Manejo de errores

- Si `GET /PayRoll/history` falla al cargar la página, el formulario igual
  funciona con los defaults estáticos (cliente/fecha/productos) y el campo
  de n° de guía queda vacío para completarlo a mano.
- Si el guardado en disco o en el JSON de historial falla durante
  `get-payroll`, se registra el error pero el PDF se devuelve igual al
  usuario (no debe bloquear la generación).
- Acceso concurrente al archivo de historial: como es una herramienta de un
  solo operador, alcanza con un lock en memoria (`lock` de C#) alrededor de
  lectura/escritura del archivo; no se requiere manejo de concurrencia
  multi-proceso.

## Testing

- Pruebas manuales (no hay suite de tests automatizados en el proyecto hoy):
  1. Generar un PDF y confirmar que se abre en pestaña nueva, se guarda en
     `App_Data/Guides/{año}/{mes}/`, y aparece en el historial.
  2. Marcar una entrada como final y confirmar que la sugerencia de
     siguiente número de guía se actualiza correctamente en un nuevo request
     a `GET /PayRoll/history`.
  3. Confirmar que dos generaciones seguidas con el mismo número de guía no
     se pisan (archivos distintos, ambas entradas visibles en el historial).
  4. Reiniciar el servidor y confirmar que el historial y los PDFs guardados
     siguen disponibles.
