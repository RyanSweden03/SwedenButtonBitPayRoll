const CLIENTS = [
  {
    id: "ak-drilling",
    label: "Ak Drilling International S.A",
    destinatary: "Ak Drilling International S.A",
    destinataryAddress: "Calle Perseo Mz J lote 12",
    destinataryDistrict: "Chorrillos",
    destinataryRUC: 20470234599,
  },
];

const CATALOG = [
  { key: "m660-512", description: "Reparación de broca para martillo 660 | 5 1/2", quantity: 5, price: 350 },
  { key: "m640-512", description: "Reparación de broca para martillo 640 | 5 1/2", quantity: 4, price: 300 },
  { key: "m545-5", description: "Reparación de broca para martillo 545 | 5", quantity: 1, price: 300 },
  { key: "m545-518", description: "Reparación de broca para martillo 545 | 5 1/8", quantity: 1, price: 300 },
  { key: "m545-514", description: "Reparación de broca para martillo 545 | 5 1/4", quantity: 1, price: 300 },
  { key: "m545-538", description: "Reparación de broca para martillo 545 | 5 3/8", quantity: 3, price: 300 },
  { key: "m545-512", description: "Reparación de broca para martillo 545 | 5 1/2", quantity: 3, price: 300 },
  { key: "sd8-778", description: "Reparación de broca para martillo SD-8 | 7 7/8", quantity: 1, price: 500 },
];

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

function todayAsInputValue() {
  const now = new Date();
  const yyyy = now.getFullYear();
  const mm = String(now.getMonth() + 1).padStart(2, "0");
  const dd = String(now.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function populateClientSelect() {
  const select = document.getElementById("client-select");
  select.innerHTML = "";

  CLIENTS.forEach((client) => {
    const option = document.createElement("option");
    option.value = client.id;
    option.textContent = client.label;
    select.appendChild(option);
  });

  const otherOption = document.createElement("option");
  otherOption.value = "other";
  otherOption.textContent = "Otro cliente…";
  select.appendChild(otherOption);
}

function applyClient(clientId) {
  const client = CLIENTS.find((c) => c.id === clientId);
  const destinatary = document.getElementById("destinatary");
  const address = document.getElementById("destinataryAddress");
  const district = document.getElementById("destinataryDistrict");
  const ruc = document.getElementById("destinataryRUC");

  if (client) {
    destinatary.value = client.destinatary;
    address.value = client.destinataryAddress;
    district.value = client.destinataryDistrict;
    ruc.value = client.destinataryRUC;
  } else {
    destinatary.value = "";
    address.value = "";
    district.value = "";
    ruc.value = "";
    destinatary.focus();
  }
}

function populateCatalogSelect() {
  const select = document.getElementById("product-catalog");
  select.innerHTML = "";

  CATALOG.forEach((item) => {
    const option = document.createElement("option");
    option.value = item.key;
    option.textContent = item.description;
    select.appendChild(option);
  });

  const otherOption = document.createElement("option");
  otherOption.value = "other";
  otherOption.textContent = "Otro producto…";
  select.appendChild(otherOption);
}

function recomputeTotals() {
  let sum = 0;

  document.querySelectorAll("#products-body tr").forEach((row) => {
    const quantity = Number(row.querySelector(".product-quantity").value) || 0;
    const price = Number(row.querySelector(".product-price").value) || 0;
    const rowTotal = quantity * price;
    row.querySelector(".row-total").textContent = money.format(rowTotal);
    sum += rowTotal;
  });

  document.getElementById("products-total").textContent = money.format(sum);
}

function addProductRow(product = { description: "", quantity: 1, price: 0 }) {
  const tbody = document.getElementById("products-body");
  const row = document.createElement("tr");

  row.innerHTML = `
    <td><input type="number" class="product-price mono" value="${product.price}" min="0" step="0.01" required /></td>
    <td><input type="number" class="product-quantity mono" value="${product.quantity}" min="1" required /></td>
    <td class="row-total mono">${money.format(product.quantity * product.price)}</td>
    <td><input type="text" class="product-description" value="${product.description}" required /></td>
    <td><button type="button" class="remove-product" aria-label="Quitar producto">✕</button></td>
  `;

  row.querySelector(".remove-product").addEventListener("click", () => {
    row.remove();
    recomputeTotals();
  });
  row.querySelector(".product-quantity").addEventListener("input", recomputeTotals);
  row.querySelector(".product-price").addEventListener("input", recomputeTotals);

  tbody.appendChild(row);
  recomputeTotals();
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
    item.className = "history-entry";
    const finalBadge = entry.isFinal ? '<span class="badge-final">Final</span>' : "";

    item.innerHTML = `
      <div class="guide-line"><span class="guide-number">Guía ${entry.guideNumber || "—"}</span> ${finalBadge}</div>
      <div class="meta">${new Date(entry.createdAt).toLocaleString()} — ${entry.payload.destinatary}</div>
      <div class="actions">
        <button type="button" class="view-pdf">Ver PDF</button>
        ${entry.isFinal ? "" : '<button type="button" class="mark-final">Marcar como final</button>'}
      </div>
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

function applyDefaults() {
  populateClientSelect();
  populateCatalogSelect();

  document.getElementById("client-select").value = CLIENTS[0]?.id ?? "other";
  applyClient(CLIENTS[0]?.id);
  document.getElementById("date").value = todayAsInputValue();

  CATALOG.forEach(addProductRow);
}

document.getElementById("payroll-form").addEventListener("submit", handleSubmit);

document.getElementById("client-select").addEventListener("change", (event) => {
  applyClient(event.target.value);
});

document.getElementById("add-from-catalog").addEventListener("click", () => {
  const select = document.getElementById("product-catalog");
  const item = CATALOG.find((c) => c.key === select.value);
  addProductRow(item ?? { description: "", quantity: 1, price: 0 });
});

applyDefaults();
loadHistory();
