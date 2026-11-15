import { initializeApp } from "https://www.gstatic.com/firebasejs/13.1.0/firebase-app.js";
import {
  getAuth,
  onAuthStateChanged,
  signInWithEmailAndPassword,
  GoogleAuthProvider,
  signInWithPopup,
  signOut
} from "https://www.gstatic.com/firebasejs/13.1.0/firebase-auth.js";
import {
  getFirestore,
  doc,
  getDoc,
  setDoc,
  onSnapshot,
  serverTimestamp,
  collection,
  query,
  where,
  orderBy,
  limit,
  updateDoc
} from "https://www.gstatic.com/firebasejs/13.1.0/firebase-firestore.js";

const firebaseConfig = {
  apiKey: "AIzaSyDyfpxmS8-nl2tQfuzHV8or8g-e7e7lx5M",
  authDomain: "zekpcsayac.firebaseapp.com",
  projectId: "zekpcsayac",
  storageBucket: "zekpcsayac.firebasestorage.app",
  messagingSenderId: "584533760664",
  appId: "1:584533760664:web:0baac8300361af57e50eed"
};

const DEVICE_ID = "zekeriyas-pc";
const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const db = getFirestore(app);

let deferredInstallPrompt = null;
const installButton = document.getElementById("installButton");
const installHelp = document.getElementById("installHelp");

function isInstalledApp() {
  return window.matchMedia("(display-mode: standalone)").matches
    || window.matchMedia("(display-mode: fullscreen)").matches
    || window.navigator.standalone === true;
}

function hideInstallControls() {
  installButton.classList.add("hidden");
  installHelp.classList.add("hidden");
}

if (isInstalledApp()) {
  hideInstallControls();
}

window.addEventListener("beforeinstallprompt", (event) => {
  event.preventDefault();
  if (isInstalledApp()) return;
  deferredInstallPrompt = event;
  installButton.classList.remove("hidden");
  installButton.textContent = "Uygulamayı yükle";
});

window.addEventListener("appinstalled", () => {
  deferredInstallPrompt = null;
  hideInstallControls();
});

window.matchMedia("(display-mode: standalone)").addEventListener?.("change", (event) => {
  if (event.matches || isInstalledApp()) hideInstallControls();
});

installButton.addEventListener("click", async () => {
  if (isInstalledApp()) {
    hideInstallControls();
    return;
  }
  if (!deferredInstallPrompt) {
    installHelp.classList.toggle("hidden");
    return;
  }

  deferredInstallPrompt.prompt();
  await deferredInstallPrompt.userChoice;
  deferredInstallPrompt = null;
});

const $ = (id) => document.getElementById(id);
const loginView = $("loginView");
const dashboardView = $("dashboardView");
const logoutButton = $("logoutButton");
let stopStateListener = null;
let stopConfigListener = null;
let stopRequestsListener = null;
let currentConfig = {};

function setMessage(id, text, type = "") {
  const element = $(id);
  element.textContent = text;
  element.className = `message ${type}`.trim();
}

function setSignedInView(user) {
  loginView.classList.toggle("hidden", Boolean(user));
  dashboardView.classList.toggle("hidden", !user);
  logoutButton.classList.toggle("hidden", !user);
}

function formatTime(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return "--:--:--";
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = Math.floor(seconds % 60);
  return [hours, minutes, secs].map((v) => String(v).padStart(2, "0")).join(":");
}

function todayKey() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

function renderDeviceState(state) {
  if (!state) {
    $("deviceStatus").textContent = "Henüz masaüstü istemcisinden durum alınmadı.";
    $("remainingTime").textContent = "--:--:--";
    $("connectionBadge").textContent = "Durum yok";
    $("connectionBadge").classList.remove("online");
    return;
  }

  $("remainingTime").textContent = formatTime(state.remainingSeconds);
  $("deviceStatus").textContent = state.isPaused ? "Duraklatıldı" : "Çalışıyor";
  $("connectionBadge").textContent = "Veri alındı";
  $("connectionBadge").classList.add("online");
}

function startListeners() {
  if (stopStateListener) stopStateListener();
  if (stopConfigListener) stopConfigListener();
  if (stopRequestsListener) stopRequestsListener();
  windowsDirty = false;

  const requestsList = $("accessRequestsList");
  const approvalPanel = requestsList.closest(".approval-panel");
  // Bekleyen istek yoksa yönetici erişimi kartını tamamen gizle.
  approvalPanel.classList.add("hidden");
  stopRequestsListener = onSnapshot(
    query(collection(db, "accessRequests"), where("status", "==", "pending"), limit(50)),
    (snapshot) => {
      requestsList.replaceChildren();
      approvalPanel.classList.toggle("hidden", snapshot.empty);
      if (snapshot.empty) {
        return;
      }
      for (const requestDoc of snapshot.docs) {
        const request = requestDoc.data();
        const row = document.createElement("div");
        row.className = "request-row";
        const details = document.createElement("div");
        const name = document.createElement("strong");
        name.textContent = request.displayName || request.email || request.uid;
        const email = document.createElement("p");
        email.className = "muted";
        email.textContent = request.email || request.uid;
        details.append(name, email);
        const actions = document.createElement("div");
        actions.className = "request-actions";
        const approve = document.createElement("button");
        approve.className = "button button-primary";
        approve.textContent = "Yetki ver";
        approve.addEventListener("click", async () => {
          approve.disabled = true;
          try {
            await setDoc(doc(db, "admins", request.uid), {
              role: "admin",
              email: request.email || "",
              displayName: request.displayName || "",
              approvedAt: serverTimestamp()
            });
            await updateDoc(doc(db, "accessRequests", request.uid), {
              status: "approved",
              reviewedAt: serverTimestamp()
            });
          } catch (error) {
            approve.disabled = false;
            setMessage("accessRequestsMessage", friendlyError(error), "error");
          }
        });
        const reject = document.createElement("button");
        reject.className = "button button-quiet";
        reject.textContent = "Reddet";
        reject.addEventListener("click", async () => {
          reject.disabled = true;
          try {
            await updateDoc(doc(db, "accessRequests", request.uid), {
              status: "rejected",
              reviewedAt: serverTimestamp()
            });
          } catch (error) {
            reject.disabled = false;
            setMessage("accessRequestsMessage", friendlyError(error), "error");
          }
        });
        actions.append(approve, reject);
        row.append(details, actions);
        requestsList.append(row);
      }
    },
    (error) => setMessage("accessRequestsMessage", friendlyError(error), "error")
  );

  stopConfigListener = onSnapshot(doc(db, "deviceConfig", DEVICE_ID), (snapshot) => {
    currentConfig = snapshot.exists() ? snapshot.data() : {};
    if (Number.isFinite(currentConfig.dailyLimitMinutes)) {
      $("dailyLimitInput").value = currentConfig.dailyLimitMinutes;
    }
    $("deviceUidInput").value = currentConfig.deviceAuthUid || "";
    $("exitPassStatus").textContent = currentConfig.exitPassword ? "Şifre belirlenmiş" : "Henüz belirlenmedi";
    // Kaydedilmemiş düzenleme varken yerel taslağın üzerine yazma.
    if (!windowsDirty) {
      loadWindowEditor(currentConfig);
    }
    $("connectionBadge").textContent = "Firebase bağlı";
    $("connectionBadge").classList.add("online");
  }, (error) => {
    $("connectionBadge").textContent = "Erişim hatası";
    $("connectionBadge").classList.remove("online");
    setMessage("dailyMessage", friendlyError(error), "error");
  });

  stopStateListener = onSnapshot(doc(db, "deviceState", DEVICE_ID), (snapshot) => {
    renderDeviceState(snapshot.exists() ? snapshot.data() : null);
  }, (error) => {
    renderDeviceState(null);
    setMessage("dailyMessage", friendlyError(error), "error");
  });
}

function friendlyError(error) {
  if (error?.code === "auth/unauthorized-domain") return "Bu web adresi Firebase Authentication yetkili alan adlarında değil.";
  if (error?.code === "auth/popup-blocked") return "Tarayıcı açılır pencereyi engelledi; izin verip tekrar dene.";
  if (error?.code === "auth/invalid-credential") return "E-posta veya şifre yanlış.";
  if (error?.code === "auth/too-many-requests") return "Çok fazla deneme yapıldı. Biraz sonra tekrar dene.";
  if (error?.code === "permission-denied" || error?.code === "firestore/permission-denied") {
    return "Erişim reddedildi. Firestore kurallarını ve admins/{UID} belgesini kontrol et.";
  }
  return error?.message || "Beklenmeyen bir hata oluştu.";
}

$("googleLoginButton").addEventListener("click", async () => {
  setMessage("loginMessage", "Google ile giriş yapılıyor…");
  try {
    const provider = new GoogleAuthProvider();
    provider.setCustomParameters({ prompt: "select_account" });
    // Use popup on all platforms to avoid mobile redirect flows failing
    // when browsers restrict cross-site storage for firebaseapp.com.
    await signInWithPopup(auth, provider);
    setMessage("loginMessage", "");
  } catch (error) {
    setMessage("loginMessage", friendlyError(error), "error");
  }
});

$("loginForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  setMessage("loginMessage", "Giriş yapılıyor…");
  try {
    await signInWithEmailAndPassword(auth, $("emailInput").value.trim(), $("passwordInput").value);
    setMessage("loginMessage", "");
  } catch (error) {
    setMessage("loginMessage", friendlyError(error), "error");
  }
});

logoutButton.addEventListener("click", async () => {
  if (stopStateListener) stopStateListener();
  if (stopConfigListener) stopConfigListener();
  if (stopRequestsListener) stopRequestsListener();
  stopStateListener = null;
  stopConfigListener = null;
  stopRequestsListener = null;
  await signOut(auth);
});

$("saveDailyButton").addEventListener("click", async () => {
  const minutes = Number($("dailyLimitInput").value);
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > 1440) {
    setMessage("dailyMessage", "0–1440 arasında tam dakika gir.", "error");
    return;
  }

  try {
    await setDoc(doc(db, "deviceConfig", DEVICE_ID), {
      dailyLimitMinutes: minutes,
      dailyLimitUpdatedAt: serverTimestamp(),
      updatedAt: serverTimestamp()
    }, { merge: true });
    setMessage("dailyMessage", "Günlük süre kaydedildi. Masaüstü senkronizasyonu henüz eklenmedi.", "success");
  } catch (error) {
    setMessage("dailyMessage", friendlyError(error), "error");
  }
});

$("setRemainingButton").addEventListener("click", async () => {
  const minutes = Number($("remainingMinutesInput").value);
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > 1440) {
    setMessage("remainingMessage", "0–1440 arasında tam dakika gir.", "error");
    return;
  }

  const button = $("setRemainingButton");
  button.disabled = true;
  try {
    await setDoc(doc(db, "deviceState", DEVICE_ID), {
      requestedRemainingSeconds: minutes * 60,
      remainingCommandId: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      remainingCommandAt: serverTimestamp()
    }, { merge: true });
    setMessage("remainingMessage", minutes === 0
      ? "Süre sıfırlama komutu gönderildi."
      : `Kalan süre ${minutes} dakika olarak ayarlandı. Bilgisayar çevrimiçi olduğunda uygulanacak.`, "success");
  } catch (error) {
    setMessage("remainingMessage", friendlyError(error), "error");
  } finally {
    button.disabled = false;
  }
});

$("addBonusButton").addEventListener("click", async () => {
  const minutes = Number($("bonusInput").value);
  if (!Number.isInteger(minutes) || minutes < 1 || minutes > 1440) {
    setMessage("bonusMessage", "1–1440 arasında tam dakika gir.", "error");
    return;
  }

  try {
    const ref = doc(db, "deviceConfig", DEVICE_ID);
    const snapshot = await getDoc(ref);
    const existing = snapshot.exists() ? snapshot.data() : {};
    const isToday = existing.bonusDateKey === todayKey();
    const currentBonus = isToday && Number.isFinite(existing.bonusMinutes) ? existing.bonusMinutes : 0;
    await setDoc(ref, {
      bonusMinutes: currentBonus + minutes,
      bonusDateKey: todayKey(),
      updatedAt: serverTimestamp()
    }, { merge: true });
    $("bonusInput").value = "15";
    setMessage("bonusMessage", `${minutes} dakika ek süre kaydedildi. Bugünkü panel toplamı: ${currentBonus + minutes} dakika.`, "success");
  } catch (error) {
    setMessage("bonusMessage", friendlyError(error), "error");
  }
});

const EXIT_PASSWORD_ITERATIONS = 150000;

function toBase64(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

// Şifre yalnızca türetilmiş PBKDF2 hash'i olarak saklanır; düz metin hiçbir yere yazılmaz.
async function deriveExitPassword(password) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations: EXIT_PASSWORD_ITERATIONS },
    key,
    256
  );
  return {
    algo: "PBKDF2-SHA256",
    iterations: EXIT_PASSWORD_ITERATIONS,
    saltB64: toBase64(salt),
    hashB64: toBase64(new Uint8Array(bits))
  };
}

$("saveExitPassButton").addEventListener("click", async () => {
  const password = $("exitPassInput").value;
  const repeat = $("exitPassRepeatInput").value;
  if (password.length < 6) {
    setMessage("exitPassMessage", "Şifre en az 6 karakter olmalı.", "error");
    return;
  }
  if (password !== repeat) {
    setMessage("exitPassMessage", "İki şifre alanı eşleşmiyor.", "error");
    return;
  }

  const button = $("saveExitPassButton");
  button.disabled = true;
  try {
    const exitPassword = await deriveExitPassword(password);
    await setDoc(doc(db, "deviceConfig", DEVICE_ID), {
      exitPassword,
      exitPasswordUpdatedAt: serverTimestamp(),
      updatedAt: serverTimestamp()
    }, { merge: true });
    setMessage("exitPassMessage", "Çıkış şifresi kaydedildi.", "success");
  } catch (error) {
    setMessage("exitPassMessage", friendlyError(error), "error");
  } finally {
    $("exitPassInput").value = "";
    $("exitPassRepeatInput").value = "";
    button.disabled = false;
  }
});

$("saveDeviceUidButton").addEventListener("click", async () => {
  const uid = $("deviceUidInput").value.trim();
  if (!uid) {
    setMessage("deviceUidMessage", "Önce masaüstü için oluşturduğun Firebase kullanıcısının UID'sini gir.", "error");
    return;
  }

  try {
    await setDoc(doc(db, "deviceConfig", DEVICE_ID), {
      deviceAuthUid: uid,
      updatedAt: serverTimestamp()
    }, { merge: true });
    setMessage("deviceUidMessage", "Masaüstü cihaz UID'si kaydedildi.", "success");
  } catch (error) {
    setMessage("deviceUidMessage", friendlyError(error), "error");
  }
});

// Kullanım saatleri: deviceConfig.allowedWindowsEnabled / allowedWindows (days: 1=Pzt..7=Paz).
const WEEK_DAYS = [
  { value: 1, label: "Pzt" },
  { value: 2, label: "Sal" },
  { value: 3, label: "Çar" },
  { value: 4, label: "Per" },
  { value: 5, label: "Cum" },
  { value: 6, label: "Cmt" },
  { value: 7, label: "Paz" }
];
const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/;
let windowRows = [];
let windowsDirty = false;

function markWindowsDirty() {
  windowsDirty = true;
  setMessage("windowsMessage", "");
}

function parseWindowRows(config) {
  const source = Array.isArray(config.allowedWindows) ? config.allowedWindows : [];
  return source.map((item) => ({
    days: Array.isArray(item?.days)
      ? item.days.filter((day) => Number.isInteger(day) && day >= 1 && day <= 7)
      : [],
    start: typeof item?.start === "string" ? item.start : "",
    end: typeof item?.end === "string" ? item.end : ""
  }));
}

function updateWindowsToggleText() {
  $("windowsEnabledText").textContent = $("windowsEnabledInput").checked ? "Açık" : "Kapalı";
}

function loadWindowEditor(config) {
  $("windowsEnabledInput").checked = config.allowedWindowsEnabled === true;
  windowRows = parseWindowRows(config);
  updateWindowsToggleText();
  renderWindowRows();
}

function windowNoteText(row) {
  if (TIME_PATTERN.test(row.start) && TIME_PATTERN.test(row.end) && row.end < row.start) {
    return `(ertesi gün ${row.end}'e kadar)`;
  }
  return "";
}

function renderWindowRows() {
  const list = $("windowsList");
  list.replaceChildren();
  if (windowRows.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted";
    empty.textContent = "Henüz aralık eklenmedi.";
    list.append(empty);
    return;
  }

  windowRows.forEach((row, index) => {
    const card = document.createElement("div");
    card.className = "window-row";

    const days = document.createElement("div");
    days.className = "day-chips";
    for (const day of WEEK_DAYS) {
      const chip = document.createElement("label");
      chip.className = "day-chip";
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = row.days.includes(day.value);
      box.addEventListener("change", () => {
        row.days = box.checked
          ? [...row.days, day.value].sort((a, b) => a - b)
          : row.days.filter((value) => value !== day.value);
        markWindowsDirty();
      });
      const text = document.createElement("span");
      text.textContent = day.label;
      chip.append(box, text);
      days.append(chip);
    }

    const times = document.createElement("div");
    times.className = "time-fields";
    const note = document.createElement("span");
    note.className = "window-note";
    const startInput = document.createElement("input");
    startInput.type = "time";
    startInput.value = row.start;
    startInput.setAttribute("aria-label", `${index + 1}. satır başlangıç saati`);
    const endInput = document.createElement("input");
    endInput.type = "time";
    endInput.value = row.end;
    endInput.setAttribute("aria-label", `${index + 1}. satır bitiş saati`);
    const separator = document.createElement("span");
    separator.className = "time-sep";
    separator.textContent = "–";
    const updateNote = () => { note.textContent = windowNoteText(row); };
    startInput.addEventListener("input", () => {
      row.start = startInput.value;
      updateNote();
      markWindowsDirty();
    });
    endInput.addEventListener("input", () => {
      row.end = endInput.value;
      updateNote();
      markWindowsDirty();
    });
    updateNote();
    times.append(startInput, separator, endInput, note);

    const footer = document.createElement("div");
    footer.className = "window-row-footer";
    const removeButton = document.createElement("button");
    removeButton.className = "button button-quiet";
    removeButton.type = "button";
    removeButton.textContent = "Sil";
    removeButton.addEventListener("click", () => {
      windowRows.splice(index, 1);
      markWindowsDirty();
      renderWindowRows();
    });
    footer.append(removeButton);

    card.append(days, times, footer);
    list.append(card);
  });
}

function validateWindowRows() {
  for (let index = 0; index < windowRows.length; index += 1) {
    const row = windowRows[index];
    const label = `${index + 1}. satır`;
    if (row.days.length === 0) return `${label}: en az bir gün seç.`;
    if (!TIME_PATTERN.test(row.start) || !TIME_PATTERN.test(row.end)) {
      return `${label}: başlangıç ve bitiş saatini gir.`;
    }
    if (row.start === row.end) return `${label}: başlangıç ve bitiş saati aynı olamaz.`;
  }
  return "";
}

$("windowsEnabledInput").addEventListener("change", () => {
  updateWindowsToggleText();
  markWindowsDirty();
});

$("addWindowButton").addEventListener("click", () => {
  windowRows.push({ days: [], start: "09:00", end: "18:00" });
  markWindowsDirty();
  renderWindowRows();
});

$("windowsTemplateButton").addEventListener("click", () => {
  windowRows = [
    { days: [1, 2, 3, 4, 5], start: "15:00", end: "20:30" },
    { days: [6, 7], start: "10:00", end: "22:00" }
  ];
  $("windowsEnabledInput").checked = true;
  updateWindowsToggleText();
  markWindowsDirty();
  renderWindowRows();
});

$("saveWindowsButton").addEventListener("click", async () => {
  const error = validateWindowRows();
  if (error) {
    setMessage("windowsMessage", error, "error");
    return;
  }

  const button = $("saveWindowsButton");
  button.disabled = true;
  try {
    await setDoc(doc(db, "deviceConfig", DEVICE_ID), {
      allowedWindowsEnabled: $("windowsEnabledInput").checked,
      allowedWindows: windowRows.map((row) => ({
        days: [...row.days].sort((a, b) => a - b),
        start: row.start,
        end: row.end
      })),
      updatedAt: serverTimestamp()
    }, { merge: true });
    windowsDirty = false;
    setMessage("windowsMessage", "Kullanım saatleri kaydedildi.", "success");
  } catch (error) {
    setMessage("windowsMessage", friendlyError(error), "error");
  } finally {
    button.disabled = false;
  }
});

onAuthStateChanged(auth, async (user) => {
  if (!user) {
    setSignedInView(null);
    if (stopStateListener) stopStateListener();
    if (stopConfigListener) stopConfigListener();
    if (stopRequestsListener) stopRequestsListener();
    stopStateListener = null;
    stopConfigListener = null;
    stopRequestsListener = null;
    return;
  }

  try {
    // The original owner is explicitly trusted in Firestore rules.
    // Other admins are approved through documents in the ASCII-named admins collection.
    const adminSnapshot = await getDoc(doc(db, "admins", user.uid));
    const isOwner = user.uid === "XkpxI8WALiaICxZek9gI9ka7Pd22";

    if (!adminSnapshot.exists() && !isOwner) {
      const requestRef = doc(db, "accessRequests", user.uid);
      const existingRequest = await getDoc(requestRef);
      if (!existingRequest.exists() || existingRequest.data().status === "rejected") {
        await setDoc(requestRef, {
          uid: user.uid,
          email: user.email || "",
          displayName: user.displayName || "",
          photoURL: user.photoURL || "",
          status: "pending",
          createdAt: serverTimestamp()
        });
      }
      setSignedInView(null);
      setMessage(
        "loginMessage",
        existingRequest.exists() && existingRequest.data().status === "approved"
          ? "Yetkin tanımlanıyor. Birkaç saniye sonra tekrar giriş yap."
          : "Yetki isteğin gönderildi. Muhammed kendi hesabından giriş yaptığında bu isteği görüp onaylayabilir.",
        "success"
      );
      await signOut(auth);
      return;
    }

    setMessage("loginMessage", "");
    setSignedInView(user);
    startListeners();
  } catch (error) {
    setSignedInView(null);
    setMessage("loginMessage", friendlyError(error), "error");
    await signOut(auth);
  }
});
