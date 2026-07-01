let design = null;
let currentGroup = "idle";
let currentFrame = 0;
let frames = [];
let challengeDesign = null;
let challengeTemplates = {challenges: [], levelConfigs: [], biomes: []};
let currentIsland = 0;
let challengeIdEdited = false;

const $ = (id) => document.getElementById(id);

function getPath(obj, path) {
  return path.split(".").reduce((acc, part) => acc?.[Number.isInteger(+part) ? +part : part], obj);
}

function setPath(obj, path, value) {
  const parts = path.split(".");
  let cur = obj;
  for (let i = 0; i < parts.length - 1; i++) {
    const key = Number.isInteger(+parts[i]) ? +parts[i] : parts[i];
    cur = cur[key];
  }
  const last = parts[parts.length - 1];
  cur[Number.isInteger(+last) ? +last : last] = value;
}

async function api(path, options = {}) {
  const res = await fetch(path, options);
  if (!res.ok) throw new Error(await res.text());
  return await res.json();
}

function bindControls() {
  $("nameInput").value = design.name || "";
  $("nameInput").oninput = () => {
    design.name = $("nameInput").value;
    syncJson();
  };

  const select = $("animationSelect");
  select.innerHTML = "";
  for (const group of Object.keys(design.animations)) {
    const opt = document.createElement("option");
    opt.value = group;
    opt.textContent = `${group} (${design.animations[group]})`;
    select.appendChild(opt);
  }
  select.value = currentGroup;
  select.onchange = () => {
    currentGroup = select.value;
    currentFrame = 0;
    refreshPreview();
  };

  document.querySelectorAll("[data-path]").forEach((input) => {
    const path = input.getAttribute("data-path");
    input.value = getPath(design, path);
    input.oninput = () => {
      const value = input.type === "number" || input.type === "range" ? Number(input.value) : input.value;
      setPath(design, path, value);
      syncJson();
      refreshPreviewDebounced();
    };
  });

  const palette = $("paletteControls");
  palette.innerHTML = "";
  for (const [key, value] of Object.entries(design.palette)) {
    const label = document.createElement("label");
    label.textContent = key;
    const input = document.createElement("input");
    input.type = "color";
    input.value = value.slice(0, 7);
    input.oninput = () => {
      const alpha = design.palette[key].length === 9 ? design.palette[key].slice(7) : "ff";
      design.palette[key] = input.value + alpha;
      syncJson();
      refreshPreviewDebounced();
    };
    label.appendChild(input);
    palette.appendChild(label);
  }
}

function syncJson() {
  $("jsonEditor").value = JSON.stringify(design, null, 2);
}

function showTab(which) {
  const mount = which === "mount";
  $("mountView").classList.toggle("hidden", !mount);
  $("challengeView").classList.toggle("hidden", mount);
  $("mountTab").classList.toggle("active", mount);
  $("challengeTab").classList.toggle("active", !mount);
  $("saveBtn").style.display = mount ? "" : "none";
  $("exportBtn").style.display = mount ? "" : "none";
}

let previewTimer = null;
function refreshPreviewDebounced() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(refreshPreview, 100);
}

async function refreshPreview() {
  $("status").textContent = "Rendering...";
  const result = await api("/api/preview", {
    method: "POST",
    headers: {"Content-Type": "application/json"},
    body: JSON.stringify({design, group: currentGroup})
  });
  frames = result.frames;
  currentFrame = Math.min(currentFrame, frames.length - 1);
  $("previewFrame").src = frames[currentFrame];
  const strip = $("filmstrip");
  strip.innerHTML = "";
  frames.forEach((src, idx) => {
    const img = document.createElement("img");
    img.src = src;
    img.title = `${currentGroup}_${idx}`;
    img.onclick = () => {
      currentFrame = idx;
      $("previewFrame").src = frames[currentFrame];
    };
    strip.appendChild(img);
  });
  $("status").textContent = `${currentGroup}: ${frames.length} frames`;
}

function animate() {
  if (frames.length > 0) {
    currentFrame = (currentFrame + 1) % frames.length;
    $("previewFrame").src = frames[currentFrame];
  }
  setTimeout(animate, currentGroup === "run" ? 90 : 140);
}

async function loadReferences() {
  const data = await api("/api/references");
  const box = $("references");
  box.innerHTML = "";
  for (const ref of data.references) {
    const img = document.createElement("img");
    img.src = ref.url;
    img.title = `${ref.kind}: ${ref.name}`;
    box.appendChild(img);
  }
}

function challengeFieldSpecs() {
  return [
    ["startingCoins", "Coins", "number"],
    ["startingBeggars", "Beggars", "number"],
    ["startingPeasants", "Peasants", "number"],
    ["startingGems", "Gems", "number"],
    ["freeBoatParts", "Boat parts", "number"],
    ["incomeMultiplier", "Income x", "number", "0.05"],
    ["caveEscapeTimer", "Cave timer", "number", "1"],
    ["minLevelWidth", "Min width", "number"],
    ["gemCount", "Gem count", "number"],
    ["seasonChangeDays", "Season days", "number", "0.25"],
    ["sideDistributionBias", "Side bias", "number", "0.25"],
    ["twoCliffs", "Two cliffs", "checkbox"],
    ["caveless", "Caveless", "checkbox"],
    ["riverless", "Riverless", "checkbox"],
    ["randomizeCliffSide", "Randomize cliff", "checkbox"],
  ];
}

function syncChallengeJson() {
  $("challengeJsonEditor").value = JSON.stringify(challengeDesign, null, 2);
}

function slugify(value) {
  return String(value || "custom-challenge").trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "custom-challenge";
}

function selectedIsland() {
  if (!challengeDesign.islands || challengeDesign.islands.length === 0) {
    challengeDesign.islands = [{}];
  }
  currentIsland = Math.max(0, Math.min(currentIsland, challengeDesign.islands.length - 1));
  return challengeDesign.islands[currentIsland];
}

function bindChallengeControls() {
  $("challengeName").value = challengeDesign.name || "";
  $("challengeId").value = challengeDesign.id || "";
  $("challengeDescription").value = challengeDesign.description || "";
  $("challengeName").oninput = () => {
    challengeDesign.name = $("challengeName").value;
    if (!challengeIdEdited) {
      challengeDesign.id = `custom.${slugify(challengeDesign.name)}`;
      $("challengeId").value = challengeDesign.id;
    }
    syncChallengeJson();
    refreshChallengePreviewDebounced();
  };
  $("challengeId").oninput = () => { challengeIdEdited = true; challengeDesign.id = $("challengeId").value; syncChallengeJson(); };
  $("challengeDescription").oninput = () => { challengeDesign.description = $("challengeDescription").value; syncChallengeJson(); };

  const baseChallenge = $("baseChallenge");
  baseChallenge.innerHTML = "";
  for (const c of challengeTemplates.challenges) {
    const opt = document.createElement("option");
    opt.value = c.assetName;
    opt.textContent = `${c.assetName} (${c.challengeType})`;
    baseChallenge.appendChild(opt);
  }
  baseChallenge.value = challengeDesign.baseChallenge || "";
  baseChallenge.onchange = () => {
    const chosen = challengeTemplates.challenges.find(c => c.assetName === baseChallenge.value);
    challengeDesign.baseChallenge = baseChallenge.value;
    if (chosen) {
      challengeDesign.baseChallengeId = chosen.id;
      challengeDesign.baseChallengeType = chosen.challengeType;
      challengeDesign.isMultiplayer = chosen.isMultiplayer;
      challengeDesign.includeHermits = chosen.includeHermits;
      challengeDesign.zombieMode = chosen.zombieMode;
      challengeDesign.startingCurrencyBagType = chosen.startingCurrencyBagType || "Bag";
    }
    bindChallengeControls();
    syncChallengeJson();
    refreshChallengePreviewDebounced();
  };

  document.querySelectorAll("[data-challenge-path]").forEach((input) => {
    const path = input.getAttribute("data-challenge-path");
    if (input.type === "checkbox") input.checked = Boolean(challengeDesign[path]);
    else input.value = challengeDesign[path] ?? "";
    input.oninput = () => {
      challengeDesign[path] = input.type === "checkbox" ? input.checked : Number(input.value);
      syncChallengeJson();
      refreshChallengePreviewDebounced();
    };
  });

  const islandSelect = $("islandSelect");
  islandSelect.innerHTML = "";
  challengeDesign.islands.forEach((island, idx) => {
    const opt = document.createElement("option");
    opt.value = String(idx);
    opt.textContent = island.name || `Island ${idx + 1}`;
    islandSelect.appendChild(opt);
  });
  islandSelect.value = String(currentIsland);
  islandSelect.onchange = () => {
    currentIsland = Number(islandSelect.value);
    bindChallengeControls();
  };

  bindIslandControls();
  syncChallengeJson();
  renderTemplateList();
}

function bindIslandControls() {
  const island = selectedIsland();
  $("islandName").value = island.name || "";
  $("islandName").oninput = () => {
    island.name = $("islandName").value;
    bindChallengeControls();
    syncChallengeJson();
    refreshChallengePreviewDebounced();
  };

  const baseLevel = $("baseLevelConfig");
  baseLevel.innerHTML = "";
  for (const level of challengeTemplates.levelConfigs) {
    const opt = document.createElement("option");
    opt.value = level.assetName;
    opt.textContent = `${level.assetName} (${level.minLevelWidth}px, gems ${level.gemCount})`;
    baseLevel.appendChild(opt);
  }
  baseLevel.value = island.baseLevelConfig || challengeDesign.baseLevelConfig || "";
  baseLevel.onchange = () => {
    const chosen = challengeTemplates.levelConfigs.find(l => l.assetName === baseLevel.value);
    if (chosen) {
      Object.assign(island, islandFromTemplate(chosen, island.name || `Island ${currentIsland + 1}`));
      challengeDesign.baseLevelConfig = chosen.assetName;
    } else {
      island.baseLevelConfig = baseLevel.value;
    }
    bindChallengeControls();
    syncChallengeJson();
    refreshChallengePreviewDebounced();
  };

  const fields = document.querySelector(".islandFields");
  fields.innerHTML = "";
  for (const [key, labelText, type, step] of challengeFieldSpecs()) {
    const label = document.createElement("label");
    label.textContent = labelText;
    const input = document.createElement("input");
    input.type = type;
    if (step) input.step = step;
    if (type === "checkbox") {
      input.checked = Boolean(island[key]);
      input.oninput = () => { island[key] = input.checked; syncChallengeJson(); refreshChallengePreviewDebounced(); };
    } else {
      input.value = island[key] ?? 0;
      input.oninput = () => { island[key] = Number(input.value); syncChallengeJson(); refreshChallengePreviewDebounced(); };
    }
    label.appendChild(input);
    fields.appendChild(label);
  }
}

function islandFromTemplate(level, name) {
  const copy = {...level};
  copy.name = name;
  copy.baseLevelConfig = copy.assetName || copy.baseLevelConfig || "";
  delete copy.assetName;
  delete copy.landCycleData;
  delete copy.alternateLandCycleData;
  return copy;
}

function renderTemplateList() {
  const list = $("templateList");
  list.innerHTML = "";
  for (const c of challengeTemplates.challenges.slice(0, 16)) {
    const card = document.createElement("div");
    card.className = "templateCard";
    card.innerHTML = `<b>${c.assetName}</b><br>${c.challengeType}<br>${(c.levelConfigs || []).join(", ")}`;
    list.appendChild(card);
  }
}

let challengePreviewTimer = null;
function refreshChallengePreviewDebounced() {
  clearTimeout(challengePreviewTimer);
  challengePreviewTimer = setTimeout(refreshChallengePreview, 120);
}

async function refreshChallengePreview() {
  $("challengeStatus").textContent = "Validating...";
  const result = await api("/api/challenge/preview", {
    method: "POST",
    headers: {"Content-Type": "application/json"},
    body: JSON.stringify({design: challengeDesign})
  });
  challengeDesign = result.design;
  const box = $("challengeSummary");
  box.innerHTML = "";
  const headline = document.createElement("div");
  headline.className = "summaryCard";
  headline.innerHTML = `<strong>${result.summary}</strong><br>Seed ${challengeDesign.challengeSeed}, multiplayer ${challengeDesign.isMultiplayer ? "on" : "off"}.`;
  box.appendChild(headline);
  challengeDesign.islands.forEach((island, idx) => {
    const card = document.createElement("div");
    card.className = "summaryCard";
    card.innerHTML = `<strong>${idx + 1}. ${island.name}</strong><br>${island.baseLevelConfig}<br>coins ${island.startingCoins}, beggars ${island.startingBeggars}, gems ${island.startingGems}, width ${island.minLevelWidth}`;
    box.appendChild(card);
  });
  $("challengeStatus").textContent = "Ready";
  syncChallengeJson();
}

async function initChallengeDesigner() {
  challengeTemplates = await api("/api/challenge/templates");
  const data = await api("/api/challenge/design");
  challengeDesign = data.design;
  bindChallengeControls();
  await refreshChallengePreview();

  $("addIslandBtn").onclick = () => {
    const template = challengeTemplates.levelConfigs[0] || {};
    challengeDesign.islands.push(islandFromTemplate(template, `Island ${challengeDesign.islands.length + 1}`));
    currentIsland = challengeDesign.islands.length - 1;
    bindChallengeControls();
    refreshChallengePreviewDebounced();
  };
  $("removeIslandBtn").onclick = () => {
    if (challengeDesign.islands.length <= 1) return;
    challengeDesign.islands.splice(currentIsland, 1);
    currentIsland = Math.max(0, currentIsland - 1);
    bindChallengeControls();
    refreshChallengePreviewDebounced();
  };
  $("applyChallengeJsonBtn").onclick = async () => {
    challengeDesign = JSON.parse($("challengeJsonEditor").value);
    challengeIdEdited = false;
    currentIsland = 0;
    bindChallengeControls();
    await refreshChallengePreview();
  };
  $("exportChallengeBtn").onclick = async () => {
    const result = await api("/api/challenge/export", {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify({design: challengeDesign})
    });
    $("challengeExportLog").textContent = `Exported ${result.filename}\nWorkspace: ${result.workspacePath}\nF1 import: ${result.gamePath || "game not auto-detected"}`;
  };
}

async function init() {
  const data = await api("/api/design");
  design = data.design;
  currentGroup = Object.keys(design.animations)[0] || "idle";
  bindControls();
  syncJson();
  await refreshPreview();
  await loadReferences();
  await initChallengeDesigner();
  animate();

  $("mountTab").onclick = () => showTab("mount");
  $("challengeTab").onclick = () => showTab("challenge");
  showTab("mount");

  $("applyJsonBtn").onclick = async () => {
    design = JSON.parse($("jsonEditor").value);
    bindControls();
    await refreshPreview();
  };
  $("saveBtn").onclick = async () => {
    const result = await api("/api/save-design", {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify({design})
    });
    $("exportLog").textContent = `Saved ${result.path}`;
  };
  $("exportBtn").onclick = async () => {
    const result = await api("/api/export", {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify({design})
    });
    $("exportLog").textContent = `Exported ${result.frames} frames to ${result.out}\nSprite sheet: ${result.sheet}\nPreview GIF: ${result.gif}\nDesign JSON: ${result.design}`;
    await loadReferences();
  };
  $("extractBtn").onclick = async () => {
    $("exportLog").textContent = "Extracting private references...";
    const result = await api("/api/extract", {method: "POST"});
    $("exportLog").textContent = result.message || JSON.stringify(result, null, 2);
    await loadReferences();
  };
}

init().catch((err) => {
  $("status").textContent = String(err);
  console.error(err);
});
