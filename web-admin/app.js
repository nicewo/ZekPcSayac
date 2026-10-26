import { initializeApp } from "https://www.gstatic.com/firebasejs/13.1.0/firebase-app.js";
import {
  getAuth,
  onAuthStateChanged,
  signInWithEmailAndPassword,
  signOut
} from "https://www.gstatic.com/firebasejs/13.1.0/firebase-auth.js";
import {
  getFirestore,
  doc,
  getDoc,
  setDoc,
  onSnapshot,
  serverTimestamp
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

const $ = (id) => document.getElementById(id);
const loginView = $("loginView");
const dashboardView = $("dashboardView");
const logoutButton = $("logoutButton");
let stopStateListener = null;
let stopConfigListener = null;
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

  stopConfigListener = onSnapshot(doc(db, "deviceConfig", DEVICE_ID), (snapshot) => {
    currentConfig = snapshot.exists() ? snapshot.data() : {};
    if (Number.isFinite(currentConfig.dailyLimitMinutes)) {
      $("dailyLimitInput").value = currentConfig.dailyLimitMinutes;
    }
    $("deviceUidInput").value = currentConfig.deviceAuthUid || "";
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
  if (error?.code === "auth/invalid-credential") return "E-posta veya şifre yanlış.";
  if (error?.code === "auth/too-many-requests") return "Çok fazla deneme yapıldı. Biraz sonra tekrar dene.";
  if (error?.code === "permission-denied" || error?.code === "firestore/permission-denied") {
    return "Erişim reddedildi. Firestore kurallarını ve admins/{UID} belgesini kontrol et.";
  }
  return error?.message || "Beklenmeyen bir hata oluştu.";
}

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
  stopStateListener = null;
  stopConfigListener = null;
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

onAuthStateChanged(auth, (user) => {
  setSignedInView(user);
  if (user) {
    startListeners();
  } else {
    if (stopStateListener) stopStateListener();
    if (stopConfigListener) stopConfigListener();
    stopStateListener = null;
    stopConfigListener = null;
  }
});
