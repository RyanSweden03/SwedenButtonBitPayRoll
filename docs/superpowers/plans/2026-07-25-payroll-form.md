# Formulario de Guías de Pago con Historial — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a web form (served by the existing `SwedenBttnBit` ASP.NET Core project) that generates payroll guides via the existing `/PayRoll/get-payroll` endpoint, prefilled with recurring client/product data, and backed by a versioned history of every generated PDF.

**Architecture:** A JSON-file-backed history store (`App_Data/payroll-history.json`) and a year/month PDF archive (`App_Data/Guides/{yyyy}/{MM}/...`) plug into the existing `PayRollController`. Three new endpoints (`GET /PayRoll/history`, `POST /PayRoll/history/{id}/final`, `GET /PayRoll/history/{id}/pdf`) expose that history. A static HTML/JS page in `wwwroot/` (no build step, no framework) drives the form and history panel.

**Tech Stack:** ASP.NET Core (net6.0) Web API, `System.Text.Json`, plain HTML/CSS/JS served via `UseStaticFiles`.

## Global Constraints

- Target framework stays `net6.0` (no framework migration in this feature).
- No new NuGet dependencies — persistence uses `System.Text.Json` + plain file I/O, not EF Core/SQLite.
- No new automated test project exists in the repo (`SwedenBttnBit.sln` has a single project) and the approved spec's Testing section calls for manual verification only — every task below is verified by running the app and checking behavior via curl/browser, not by adding a test framework.
- Single-user internal tool: no authentication, no multi-process file-locking concerns (an in-process `lock` around the history file is sufficient).
- Frontend is plain HTML/CSS/JS with no build step or framework, matching the approved design.
- Full design reference: `docs/superpowers/specs/2026-07-25-payroll-form-design.md`.

---

### Task 1: History domain model + JSON-backed store + `GET /PayRoll/history`

**Files:**
- Create: `SwedenBttnBit/Domain/PayRollHistoryEntry.cs`
- Create: `SwedenBttnBit/Services/PayRollHistoryStore.cs`
- Modify: `SwedenBttnBit/Program.cs`
- Modify: `SwedenBttnBit/Controllers/PayRollController.cs`
- Create: `.gitignore` (repo root)

**Interfaces:**
- Produces: `SwedenBttnBit.Domain.PayRollHistoryEntry` with properties `Id (string)`, `GuideNumber (string)`, `CreatedAt (DateTime)`, `IsFinal (bool)`, `PdfRelativePath (string)`, `Payload (PayRoll)`.
- Produces: `SwedenBttnBit.Services.IPayRollHistoryStore` with `GetAll(): IReadOnlyList<PayRollHistoryEntry>`, `GetById(string id): PayRollHistoryEntry?`, `Add(string id, PayRoll payload, string pdfRelativePath): PayRollHistoryEntry`, `MarkFinal(string id): bool`. Task 2 and Task 3 consume this interface.

- [ ] **Step 1: Create the history entry model**

```csharp
// SwedenBttnBit/Domain/PayRollHistoryEntry.cs
namespace SwedenBttnBit.Domain
{
    public class PayRollHistoryEntry
    {
        public string Id { get; set; } = string.Empty;
        public string GuideNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsFinal { get; set; }
        public string PdfRelativePath { get; set; } = string.Empty;
        public PayRoll Payload { get; set; } = new PayRoll();
    }
}
```

- [ ] **Step 2: Create the JSON-backed store**

```csharp
// SwedenBttnBit/Services/PayRollHistoryStore.cs
using SwedenBttnBit.Domain;
using System.Text.Json;

namespace SwedenBttnBit.Services
{
    public interface IPayRollHistoryStore
    {
        IReadOnlyList<PayRollHistoryEntry> GetAll();
        PayRollHistoryEntry? GetById(string id);
        PayRollHistoryEntry Add(string id, PayRoll payload, string pdfRelativePath);
        bool MarkFinal(string id);
    }

    public class PayRollHistoryStore : IPayRollHistoryStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        private readonly string _filePath;
        private readonly object _lock = new();

        public PayRollHistoryStore(IWebHostEnvironment env)
        {
            var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dataDir);
            _filePath = Path.Combine(dataDir, "payroll-history.json");
        }

        public IReadOnlyList<PayRollHistoryEntry> GetAll()
        {
            lock (_lock)
            {
                return Load().OrderByDescending(e => e.CreatedAt).ToList();
            }
        }

        public PayRollHistoryEntry? GetById(string id)
        {
            lock (_lock)
            {
                return Load().FirstOrDefault(e => e.Id == id);
            }
        }

        public PayRollHistoryEntry Add(string id, PayRoll payload, string pdfRelativePath)
        {
            lock (_lock)
            {
                var entries = Load();
                var entry = new PayRollHistoryEntry
                {
                    Id = id,
                    GuideNumber = payload.GuideNumber ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    IsFinal = false,
                    PdfRelativePath = pdfRelativePath,
                    Payload = payload
                };
                entries.Add(entry);
                Save(entries);
                return entry;
            }
        }

        public bool MarkFinal(string id)
        {
            lock (_lock)
            {
                var entries = Load();
                var target = entries.FirstOrDefault(e => e.Id == id);
                if (target == null) return false;

                foreach (var entry in entries.Where(e => e.GuideNumber == target.GuideNumber))
                    entry.IsFinal = entry.Id == id;

                Save(entries);
                return true;
            }
        }

        private List<PayRollHistoryEntry> Load()
        {
            if (!File.Exists(_filePath)) return new List<PayRollHistoryEntry>();

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return new List<PayRollHistoryEntry>();

            return JsonSerializer.Deserialize<List<PayRollHistoryEntry>>(json, SerializerOptions)
                   ?? new List<PayRollHistoryEntry>();
        }

        private void Save(List<PayRollHistoryEntry> entries)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, SerializerOptions));
        }
    }
}
```

- [ ] **Step 3: Register the store in `Program.cs`**

Add near the top of the file and inside the services section:

```csharp
using SwedenBttnBit.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<IPayRollHistoryStore, PayRollHistoryStore>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

- [ ] **Step 4: Inject the store into `PayRollController` and add `GET /PayRoll/history`**

Modify the constructor and add a using at the top of `SwedenBttnBit/Controllers/PayRollController.cs`:

```csharp
using SwedenBttnBit.Services;
```

```csharp
private readonly SmtpSettings _smtpSettings;
private readonly IPayRollHistoryStore _historyStore;

public PayRollController(IConfiguration configuration, IPayRollHistoryStore historyStore)
{
    _smtpSettings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>()!;
    _historyStore = historyStore;
}

[HttpGet("history")]
public IActionResult GetHistory()
{
    return Ok(_historyStore.GetAll());
}
```

(Place the `GetHistory` action right after the closing brace of `GeneratePDF`.)

- [ ] **Step 5: Add `.gitignore` for generated data**

```gitignore
SwedenBttnBit/App_Data/
```

- [ ] **Step 6: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
In another terminal:
```bash
curl http://localhost:5000/PayRoll/history
```
Expected: build succeeds with no new errors, and the curl call returns `[]` (empty JSON array — no history yet). Stop the running app afterward.

- [ ] **Step 7: Commit**

```bash
git add SwedenBttnBit/Domain/PayRollHistoryEntry.cs SwedenBttnBit/Services/PayRollHistoryStore.cs SwedenBttnBit/Program.cs SwedenBttnBit/Controllers/PayRollController.cs .gitignore
git commit -m "feat: add payroll history store and GET /PayRoll/history"
```

---

### Task 2: PDF archive on disk + auto-save on generate

**Files:**
- Create: `SwedenBttnBit/Services/GuidePdfArchive.cs`
- Modify: `SwedenBttnBit/Program.cs`
- Modify: `SwedenBttnBit/Controllers/PayRollController.cs`

**Interfaces:**
- Consumes: `IPayRollHistoryStore.Add(string id, PayRoll payload, string pdfRelativePath)` from Task 1.
- Produces: `SwedenBttnBit.Services.IGuidePdfArchive` with `Save(byte[] pdfBytes, PayRoll payroll, string id): string` (returns a path relative to the archive root) and `GetFullPath(string relativePath): string`. Task 3 consumes `GetFullPath`.

- [ ] **Step 1: Create the PDF archive**

```csharp
// SwedenBttnBit/Services/GuidePdfArchive.cs
using SwedenBttnBit.Domain;
using System.Globalization;

namespace SwedenBttnBit.Services
{
    public interface IGuidePdfArchive
    {
        string Save(byte[] pdfBytes, PayRoll payroll, string id);
        string GetFullPath(string relativePath);
    }

    public class GuidePdfArchive : IGuidePdfArchive
    {
        private readonly string _rootPath;

        public GuidePdfArchive(IWebHostEnvironment env)
        {
            _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "Guides");
        }

        public string Save(byte[] pdfBytes, PayRoll payroll, string id)
        {
            var year = payroll.Date.ToString("yyyy", CultureInfo.InvariantCulture);
            var month = payroll.Date.ToString("MM", CultureInfo.InvariantCulture);
            var folder = Path.Combine(_rootPath, year, month);
            Directory.CreateDirectory(folder);

            var fileName = SanitizeFileName(
                $"{payroll.GuideNumber}_{payroll.Date:ddMMyyyy}_{payroll.Destinatary}_{id}.pdf"
                    .Replace(" ", "_"));

            File.WriteAllBytes(Path.Combine(folder, fileName), pdfBytes);

            return Path.Combine(year, month, fileName);
        }

        public string GetFullPath(string relativePath) => Path.Combine(_rootPath, relativePath);

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
```

- [ ] **Step 2: Register the archive in `Program.cs`**

```csharp
builder.Services.AddSingleton<IGuidePdfArchive, GuidePdfArchive>();
```
(add this line right below the `IPayRollHistoryStore` registration from Task 1)

- [ ] **Step 3: Wire the archive + history save into `GeneratePDF`**

Modify the constructor of `PayRollController` again:

```csharp
private readonly SmtpSettings _smtpSettings;
private readonly IPayRollHistoryStore _historyStore;
private readonly IGuidePdfArchive _pdfArchive;

public PayRollController(IConfiguration configuration, IPayRollHistoryStore historyStore, IGuidePdfArchive pdfArchive)
{
    _smtpSettings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>()!;
    _historyStore = historyStore;
    _pdfArchive = pdfArchive;
}
```

Right after `byte[] file = memoryStream.ToArray();` in `GeneratePDF`, before the existing `// Email (opcional...)` block, add:

```csharp
// Guardado del historial: best-effort, no debe bloquear la respuesta del PDF.
try
{
    var entryId = Guid.NewGuid().ToString("N");
    var relativePath = _pdfArchive.Save(file, payroll, entryId);
    _historyStore.Add(entryId, payroll, relativePath);
}
catch
{
}
```

- [ ] **Step 4: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
In another terminal, generate a PDF (adjust the JSON body if needed, this mirrors the sample the user provided):
```bash
curl -X POST http://localhost:5000/PayRoll/get-payroll \
  -H "Content-Type: application/json" \
  -d '{"date":"2026-07-25","destinatary":"Ak Drilling International S.A","destinataryAddress":"Calle Perseo Mz J lote 12","destinataryDistrict":"Chorrillos","destinataryRUC":20470234599,"guideNumber":"142","products":[{"description":"Reparación de broca","quantity":5,"price":350}]}' \
  -o test.pdf
```
Expected: `test.pdf` is a valid PDF file. Then check the archive:
```bash
find SwedenBttnBit/App_Data/Guides -name "*.pdf"
curl http://localhost:5000/PayRoll/history
```
Expected: one `.pdf` file under `SwedenBttnBit/App_Data/Guides/2026/07/`, and the history endpoint now returns one entry with `guideNumber: "142"`, `isFinal: false`, and a populated `payload`.

Now run the exact same curl command again (same `guideNumber: "142"`) to simulate a second attempt at the same guide:
```bash
curl -X POST http://localhost:5000/PayRoll/get-payroll \
  -H "Content-Type: application/json" \
  -d '{"date":"2026-07-25","destinatary":"Ak Drilling International S.A","destinataryAddress":"Calle Perseo Mz J lote 12","destinataryDistrict":"Chorrillos","destinataryRUC":20470234599,"guideNumber":"142","products":[{"description":"Reparación de broca","quantity":5,"price":350}]}' \
  -o test2.pdf
find SwedenBttnBit/App_Data/Guides -name "*.pdf"
curl http://localhost:5000/PayRoll/history
```
Expected: two distinct `.pdf` files now exist under `SwedenBttnBit/App_Data/Guides/2026/07/` (different filenames thanks to the `{id}` suffix — neither overwrote the other), and `GET /PayRoll/history` returns two entries, both with `guideNumber: "142"`. Stop the app and delete `test.pdf`/`test2.pdf` afterward.

- [ ] **Step 5: Commit**

```bash
git add SwedenBttnBit/Services/GuidePdfArchive.cs SwedenBttnBit/Program.cs SwedenBttnBit/Controllers/PayRollController.cs
git commit -m "feat: archive generated PDFs to disk and record history on generate"
```

---

### Task 3: Mark-final and serve-saved-PDF endpoints

**Files:**
- Modify: `SwedenBttnBit/Controllers/PayRollController.cs`

**Interfaces:**
- Consumes: `IPayRollHistoryStore.MarkFinal(string id)` and `IPayRollHistoryStore.GetById(string id)` from Task 1; `IGuidePdfArchive.GetFullPath(string relativePath)` from Task 2.

- [ ] **Step 1: Add the two endpoints**

Add these actions to `PayRollController`, right after `GetHistory`:

```csharp
[HttpPost("history/{id}/final")]
public IActionResult MarkFinal(string id)
{
    return _historyStore.MarkFinal(id) ? NoContent() : NotFound();
}

[HttpGet("history/{id}/pdf")]
public IActionResult GetHistoryPdf(string id)
{
    var entry = _historyStore.GetById(id);
    if (entry == null) return NotFound();

    var fullPath = _pdfArchive.GetFullPath(entry.PdfRelativePath);
    if (!System.IO.File.Exists(fullPath)) return NotFound();

    return File(System.IO.File.ReadAllBytes(fullPath), "application/pdf");
}
```

- [ ] **Step 2: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
Generate two PDFs with different `guideNumber` values (reuse the curl command from Task 2, changing `guideNumber` to `"143"` for the second one). Then:
```bash
curl http://localhost:5000/PayRoll/history
```
Copy one entry's `id` from the response and run:
```bash
curl -X POST http://localhost:5000/PayRoll/history/<id>/final -i
curl http://localhost:5000/PayRoll/history
curl http://localhost:5000/PayRoll/history/<id>/pdf -o final-check.pdf
curl http://localhost:5000/PayRoll/history/does-not-exist/pdf -i
```
Expected: the `POST .../final` call returns `204 No Content`; the following `GET /history` shows that entry's `isFinal: true` and every other entry `isFinal: false`; `final-check.pdf` is a valid PDF matching the saved file; the request with a bogus id returns `404 Not Found`. Stop the app and delete `final-check.pdf` afterward.

- [ ] **Step 3: Commit**

```bash
git add SwedenBttnBit/Controllers/PayRollController.cs
git commit -m "feat: add mark-final and serve-saved-pdf endpoints"
```

---

### Task 4: Static file serving + form page skeleton

**Files:**
- Create: `SwedenBttnBit/wwwroot/index.html`
- Create: `SwedenBttnBit/wwwroot/styles.css`
- Modify: `SwedenBttnBit/Program.cs`

**Interfaces:**
- Produces: DOM elements `#payroll-form`, `#destinatary`, `#destinataryAddress`, `#destinataryDistrict`, `#destinataryRUC`, `#date`, `#guideNumber`, `#products-table`, `#products-body`, `#add-product`, `#generate-btn`, `#history-list`. Task 5 and Task 6 consume these element ids.

- [ ] **Step 1: Enable static file serving**

In `SwedenBttnBit/Program.cs`, add right after `var app = builder.Build();` (before the Swagger block):

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

- [ ] **Step 2: Create the page skeleton**

```html
<!-- SwedenBttnBit/wwwroot/index.html -->
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8" />
  <title>Generar Guía de Pago</title>
  <link rel="stylesheet" href="styles.css" />
</head>
<body>
  <h1>Generar Guía de Pago</h1>

  <div class="layout">
    <form id="payroll-form" class="panel">
      <fieldset>
        <legend>Cliente</legend>
        <label>Razón social
          <input type="text" id="destinatary" required />
        </label>
        <label>Dirección
          <input type="text" id="destinataryAddress" required />
        </label>
        <label>Distrito
          <input type="text" id="destinataryDistrict" required />
        </label>
        <label>RUC
          <input type="number" id="destinataryRUC" required />
        </label>
      </fieldset>

      <fieldset>
        <legend>Guía</legend>
        <label>Fecha
          <input type="date" id="date" required />
        </label>
        <label>N&deg; de guía
          <input type="text" id="guideNumber" placeholder="Sugerido según historial" />
        </label>
      </fieldset>

      <fieldset>
        <legend>Productos</legend>
        <table id="products-table">
          <thead>
            <tr>
              <th>Descripción</th>
              <th>Cantidad</th>
              <th>Precio</th>
              <th></th>
            </tr>
          </thead>
          <tbody id="products-body"></tbody>
        </table>
        <button type="button" id="add-product">+ Agregar producto</button>
      </fieldset>

      <button type="submit" id="generate-btn">Generar PDF</button>
    </form>

    <section class="panel">
      <h2>Historial</h2>
      <ul id="history-list"></ul>
    </section>
  </div>

  <script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 3: Create minimal styling**

```css
/* SwedenBttnBit/wwwroot/styles.css */
body { font-family: system-ui, sans-serif; margin: 2rem; color: #1f2937; }
h1 { font-size: 1.4rem; margin-bottom: 1rem; }
.layout { display: flex; gap: 2rem; align-items: flex-start; flex-wrap: wrap; }
.panel { background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 8px; padding: 1.2rem; }
#payroll-form { flex: 2; min-width: 380px; }
#payroll-form fieldset { border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 1rem; padding: 0.8rem 1rem; }
#payroll-form label { display: block; margin-bottom: 0.6rem; font-size: 0.85rem; color: #374151; }
#payroll-form input { display: block; width: 100%; margin-top: 0.2rem; padding: 0.4rem; border: 1px solid #d1d5db; border-radius: 4px; box-sizing: border-box; }
#products-table { width: 100%; border-collapse: collapse; margin-bottom: 0.6rem; }
#products-table th, #products-table td { padding: 0.3rem; text-align: left; }
#products-table input { width: 100%; box-sizing: border-box; }
#generate-btn { background: #111827; color: white; border: none; padding: 0.6rem 1.2rem; border-radius: 6px; cursor: pointer; }
section.panel { flex: 1; min-width: 280px; }
#history-list { list-style: none; padding: 0; margin: 0; }
#history-list li { border-bottom: 1px solid #e5e7eb; padding: 0.5rem 0; font-size: 0.85rem; }
.badge-final { background: #16a34a; color: white; border-radius: 4px; padding: 0.1rem 0.4rem; font-size: 0.7rem; margin-left: 0.4rem; }
```

Note: `wwwroot/app.js` doesn't exist yet (Task 5 creates it) — the browser check in Step 4 will show a 404 for it in the console, which is expected at this point in the plan.

- [ ] **Step 4: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
Open `http://localhost:5000/` in a browser. Expected: the page renders with the "Cliente", "Guía", and "Productos" sections empty, an empty "Historial" heading with no list items, and a 404 console error for `app.js` (expected — created next task). Stop the app afterward.

- [ ] **Step 5: Commit**

```bash
git add SwedenBttnBit/wwwroot/index.html SwedenBttnBit/wwwroot/styles.css SwedenBttnBit/Program.cs
git commit -m "feat: serve static form page skeleton"
```

---

### Task 5: Form defaults, product rows, and PDF generation

**Files:**
- Create: `SwedenBttnBit/wwwroot/app.js`

**Interfaces:**
- Consumes: DOM element ids from Task 4; `POST /PayRoll/get-payroll` from the existing controller (unchanged contract).
- Produces: `loadHistory()` as a no-op stub (Task 6 replaces its body with the real implementation) so `handleSubmit` can call it without depending on Task 6 first.

- [ ] **Step 1: Create `app.js` with defaults, product rows, and submit handling**

```javascript
// SwedenBttnBit/wwwroot/app.js

const DEFAULT_CLIENT = {
  destinatary: "Ak Drilling International S.A",
  destinataryAddress: "Calle Perseo Mz J lote 12",
  destinataryDistrict: "Chorrillos",
  destinataryRUC: 20470234599,
};

const DEFAULT_PRODUCTS = [
  { description: "Reparación de broca para martillo 660 | 5 1/2", quantity: 5, price: 350 },
  { description: "Reparación de broca para martillo 640 | 5 1/2", quantity: 4, price: 300 },
  { description: "Reparación de broca para martillo 545 | 5", quantity: 1, price: 300 },
  { description: "Reparación de broca para martillo 545 | 5 1/8", quantity: 1, price: 300 },
  { description: "Reparación de broca para martillo 545 | 5 1/4", quantity: 1, price: 300 },
  { description: "Reparación de broca para martillo 545 | 5 3/8", quantity: 3, price: 300 },
  { description: "Reparación de broca para martillo 545 | 5 1/2", quantity: 3, price: 300 },
  { description: "Reparación de broca para martillo SD-8 | 7 7/8", quantity: 1, price: 500 },
];

function todayAsInputValue() {
  const now = new Date();
  const yyyy = now.getFullYear();
  const mm = String(now.getMonth() + 1).padStart(2, "0");
  const dd = String(now.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function addProductRow(product = { description: "", quantity: 1, price: 0 }) {
  const tbody = document.getElementById("products-body");
  const row = document.createElement("tr");

  row.innerHTML = `
    <td><input type="text" class="product-description" value="${product.description}" required /></td>
    <td><input type="number" class="product-quantity" value="${product.quantity}" min="1" required /></td>
    <td><input type="number" class="product-price" value="${product.price}" min="0" step="0.01" required /></td>
    <td><button type="button" class="remove-product">Quitar</button></td>
  `;

  row.querySelector(".remove-product").addEventListener("click", () => row.remove());
  tbody.appendChild(row);
}

function collectPayload() {
  const rows = document.querySelectorAll("#products-body tr");
  const products = Array.from(rows).map((row, index) => ({
    id: index + 1,
    name: "",
    description: row.querySelector(".product-description").value,
    quantity: Number(row.querySelector(".product-quantity").value),
    price: Number(row.querySelector(".product-price").value),
  }));

  return {
    id: 0,
    date: document.getElementById("date").value,
    destinatary: document.getElementById("destinatary").value,
    destinataryAddress: document.getElementById("destinataryAddress").value,
    destinataryDistrict: document.getElementById("destinataryDistrict").value,
    destinataryRUC: Number(document.getElementById("destinataryRUC").value),
    guideNumber: document.getElementById("guideNumber").value,
    products,
  };
}

async function handleSubmit(event) {
  event.preventDefault();

  const response = await fetch("/PayRoll/get-payroll", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(collectPayload()),
  });

  if (!response.ok) {
    alert("No se pudo generar el PDF.");
    return;
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  window.open(url, "_blank");

  await loadHistory();
}

async function loadHistory() {
  // Se implementa en la siguiente tarea (panel de historial).
}

function applyDefaults() {
  document.getElementById("destinatary").value = DEFAULT_CLIENT.destinatary;
  document.getElementById("destinataryAddress").value = DEFAULT_CLIENT.destinataryAddress;
  document.getElementById("destinataryDistrict").value = DEFAULT_CLIENT.destinataryDistrict;
  document.getElementById("destinataryRUC").value = DEFAULT_CLIENT.destinataryRUC;
  document.getElementById("date").value = todayAsInputValue();

  DEFAULT_PRODUCTS.forEach(addProductRow);
}

document.getElementById("payroll-form").addEventListener("submit", handleSubmit);
document.getElementById("add-product").addEventListener("click", () => addProductRow());

applyDefaults();
loadHistory();
```

- [ ] **Step 2: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
Open `http://localhost:5000/` in a browser. Expected:
- Client fields (Razón social, Dirección, Distrito, RUC) are prefilled with the `Ak Drilling International S.A` defaults, and are editable.
- Fecha is prefilled with today's date.
- Productos shows the 8 default rows with the given descriptions/quantities/prices, each editable and removable via "Quitar"; "+ Agregar producto" adds a blank row.
- N° de guía is empty (no history yet to suggest from).
- Clicking "Generar PDF" opens a new tab with a rendered PDF matching the form's data.
Stop the app afterward.

- [ ] **Step 3: Commit**

```bash
git add SwedenBttnBit/wwwroot/app.js
git commit -m "feat: prefill form defaults and generate PDF from the browser"
```

---

### Task 6: History panel — list, view, mark final, and next-guide-number suggestion

**Files:**
- Modify: `SwedenBttnBit/wwwroot/app.js`

**Interfaces:**
- Consumes: `GET /PayRoll/history`, `POST /PayRoll/history/{id}/final`, `GET /PayRoll/history/{id}/pdf` from Tasks 1 and 3; the `#guideNumber` and `#history-list` elements from Task 4; replaces the `loadHistory()` stub from Task 5.

- [ ] **Step 1: Replace the `loadHistory` stub with the real implementation**

Replace this block in `SwedenBttnBit/wwwroot/app.js`:

```javascript
async function loadHistory() {
  // Se implementa en la siguiente tarea (panel de historial).
}
```

with:

```javascript
function computeNextGuideNumber(entries) {
  const numbers = entries
    .filter((e) => e.isFinal)
    .map((e) => Number(e.guideNumber))
    .filter((n) => Number.isInteger(n));

  if (numbers.length === 0) return "";
  return String(Math.max(...numbers) + 1);
}

function renderHistory(entries) {
  const list = document.getElementById("history-list");
  list.innerHTML = "";

  entries.forEach((entry) => {
    const item = document.createElement("li");
    const finalBadge = entry.isFinal ? '<span class="badge-final">Final</span>' : "";

    item.innerHTML = `
      <strong>Guía ${entry.guideNumber}</strong> ${finalBadge}<br />
      ${new Date(entry.createdAt).toLocaleString()} — ${entry.payload.destinatary}<br />
      <button type="button" class="view-pdf">Ver PDF</button>
      ${entry.isFinal ? "" : '<button type="button" class="mark-final">Marcar como final</button>'}
    `;

    item.querySelector(".view-pdf").addEventListener("click", () => {
      window.open(`/PayRoll/history/${entry.id}/pdf`, "_blank");
    });

    const markButton = item.querySelector(".mark-final");
    if (markButton) {
      markButton.addEventListener("click", async () => {
        await fetch(`/PayRoll/history/${entry.id}/final`, { method: "POST" });
        await loadHistory();
      });
    }

    list.appendChild(item);
  });
}

async function loadHistory() {
  const response = await fetch("/PayRoll/history");
  if (!response.ok) return;

  const entries = await response.json();
  renderHistory(entries);

  const guideNumberInput = document.getElementById("guideNumber");
  if (!guideNumberInput.value) {
    guideNumberInput.value = computeNextGuideNumber(entries);
  }
}
```

- [ ] **Step 2: Build and manually verify**

Run:
```bash
dotnet build
dotnet run --project SwedenBttnBit
```
In the browser at `http://localhost:5000/`:
1. Generate a PDF (leave N° de guía as suggested or type one, e.g. `200`). Confirm a new row appears in "Historial" with that guide number, a timestamp, and the client name, with a "Marcar como final" button and no "Final" badge.
2. Click "Ver PDF" on that row — confirm it opens the saved PDF in a new tab.
3. Click "Marcar como final" — confirm the row now shows the "Final" badge and the button disappears.
4. Reload the page — confirm N° de guía is now prefilled with `201` (the marked guide number + 1).
5. Stop the app (Ctrl+C) and run `dotnet run --project SwedenBttnBit` again. Reload `http://localhost:5000/` — confirm the "Historial" panel still shows the same entries (including the "Final" badge) and "Ver PDF" still opens the previously generated file, proving the history and archived PDFs survive a server restart.
Stop the app afterward.

- [ ] **Step 3: Commit**

```bash
git add SwedenBttnBit/wwwroot/app.js
git commit -m "feat: render history panel with view/mark-final and next guide number suggestion"
```
