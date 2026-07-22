const params = new URLSearchParams(location.search);
document.getElementById("domain").textContent = params.get("domain") || "This website";
document.getElementById("session").textContent = params.get("session") || "this Focus Session";

const end = params.get("end");
if (end) {
  const date = new Date(end);
  if (!Number.isNaN(date.getTime())) {
    document.getElementById("end").textContent = date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  }
}

