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
