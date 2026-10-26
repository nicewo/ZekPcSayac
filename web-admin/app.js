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

  const requestsList = $("accessRequestsList");
  stopRequestsListener = onSnapshot(
    query(collection(db, "accessRequests"), where("status", "==", "pending"), limit(50)),
    (snapshot) => {
      requestsList.replaceChildren();
      if (snapshot.empty) {
        requestsList.textContent = "Bekleyen yetki isteği yok.";
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
