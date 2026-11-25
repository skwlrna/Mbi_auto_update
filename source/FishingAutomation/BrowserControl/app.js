const byId = id => document.getElementById(id);
let key = "";
let busy = false;
let snapshot;
let pending;

function render(data) {
    snapshot = data;
    byId("status").textContent = data.status;
    const cards = data.orders.map(order => {
        const card = document.createElement("article");
        const name = document.createElement("strong");
        name.textContent = order.name + " · " + (order.total ? "목표 " : "추가 ") + order.quantity.toLocaleString("ko-KR") + "개";
        const detail = document.createElement("p");
        detail.textContent = [order.state, order.detail].filter(Boolean).join(" · ");
        card.append(name, detail);
        return card;
    });
    byId("orders").replaceChildren(...(cards.length ? cards : ["등록된 가공 요청이 없습니다."]));
}

async function request(path, write = false) {
    if (busy || !key) return false;
    busy = true;
    byId("start").disabled = byId("stop").disabled = byId("add").disabled = true;
    try {
        const headers = {"X-MabiAuto-Key": key};
        if (write) {
            if (pending?.path !== path) {
                const bytes = crypto.getRandomValues(new Uint8Array(16));
                const raw = Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
                pending = {path, id: raw.replace(/^(........)(....)(....)(....)(............)$/, "$1-$2-$3-$4-$5")};
            }
            headers["X-Request-Id"] = pending.id;
        }
        const response = await fetch(path, {method: write ? "POST" : "GET", headers, cache: "no-store"});
        if (response.status === 401) {
            key = "";
            byId("connect").hidden = false;
            byId("dashboard").hidden = true;
            throw new Error("접속 키를 확인하세요.");
        }
        if (!response.ok) throw new Error("PC에서 요청을 처리하지 못했습니다. 연결과 작업 기록을 확인하세요.");
        const data = await response.json();
        if (write) pending = undefined;
        render(data);
        if (data.message || byId("connect").hidden === false) byId("message").textContent = data.message ?? "";
        byId("connect").hidden = true;
        byId("dashboard").hidden = false;
        return true;
    } catch (error) {
        snapshot = undefined;
        byId("status").textContent = "상태 확인 실패";
        byId("message").textContent = error instanceof TypeError ? "PC에 연결할 수 없습니다." : error.message;
        return false;
    } finally {
        busy = false;
        byId("start").disabled = !snapshot?.canStart;
        byId("stop").disabled = !snapshot?.canStop;
        byId("add").disabled = !snapshot;
    }
}

function showOrder(open) {
    byId("home").hidden = open;
    byId("orderPane").hidden = !open;
    byId("homeTab").setAttribute("aria-pressed", String(!open));
    byId("orderTab").setAttribute("aria-pressed", String(open));
}
byId("homeTab").onclick = () => showOrder(false);
byId("orderTab").onclick = () => showOrder(true);
byId("connect").onsubmit = event => {
    event.preventDefault();
    key = byId("key").value.trim();
    byId("key").value = "";
    void request("/api/status");
};
byId("order").onsubmit = event => {
    event.preventDefault();
    const query = new URLSearchParams({
        name: byId("name").value, quantity: byId("quantity").value, mode: byId("mode").value
    });
    void request("/api/order?" + query, true);
};
byId("start").onclick = () => void request("/api/start", true);
byId("stop").onclick = () => void request("/api/stop", true);
setInterval(() => { if (!document.hidden && key) void request("/api/status"); }, 3000);
