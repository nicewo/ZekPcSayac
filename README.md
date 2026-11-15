# ZekPcSayac

Windows WPF application for a daily PC usage countdown.

## Firebase device synchronization

The app signs in with the Firebase **device account** (email/password), reads the configured daily allowance and bonus from `deviceConfig/zekeriyas-pc`, and synchronizes the current remaining seconds and pause state to `deviceState/zekeriyas-pc`.

- Firebase project: `zekpcsayac`
- Device document ID: `zekeriyas-pc`
- Credentials are stored encrypted for the current Windows user with Windows DPAPI in `%LOCALAPPDATA%\ZekPcSayac\firebase-login.bin`.
- The local countdown keeps working if Firebase is unavailable. Synchronization retries periodically.
- The Firebase Authentication Email/Password provider must be enabled.
- The account UID must match `deviceConfig/zekeriyas-pc.deviceAuthUid`.
- Firestore security rules must permit this device account to read its config/state and update the allowed state fields.

On first launch, enter the device account's email and password in the Firebase connection dialog. Do not use the administrator's account unless that account's UID is explicitly assigned as the device UID.

The app imports the remote remaining time when it starts, then writes the live remaining time, pause status, date key, last-seen timestamp and client version about every 15 seconds. It also checks for administrator changes about every 15 seconds. A new daily limit is used at the next daily renewal; bonus minutes granted for today are added to the current remaining time. Manual remaining-time commands from the web panel are polled and applied about every 15 seconds.

## Kurulum

1. Çocuğun Windows hesabını **Standart kullanıcı** yap (yönetici değil).
2. Yönetici olarak PowerShell aç ve proje klasöründe çalıştır:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\install-task.ps1 -UserName Zekeriya -InstallDir "C:\Program Files\ZekPcSayac"
   ```

   Script `\ZekPcSayac\Watchdog` görevini kurar: kullanıcı oturum açtığında ve ardından her dakika uygulamayı başlatmayı dener (çalışan bir örnek varsa yenisi açılmaz). Görev yükseltilmemiş (Limited) yetkiyle çalışır.

## Çıkış şifresi

Uygulamayı kapatmak için gereken şifre koda yazılmaz. Yönetici şifreyi web paneldeki ayarlardan belirler; uygulama bu şifrenin PBKDF2-SHA256 özetini `deviceConfig/zekeriyas-pc.exitPassword` alanından okur. Şifre hiçbir zaman düz metin olarak saklanmaz veya kodda bulunmaz.

## Saat aralığı

`allowedWindowsEnabled` açıksa bilgisayar yalnızca `allowedWindows` içindeki aralıklarda kullanılabilir. Her aralık gün (1=Pzt … 7=Paz), başlangıç ve bitiş saati (`HH:mm`) ile tanımlanır; başlangıç dahil, bitiş hariçtir.

- **Gece yarısını aşan aralıklar:** Bitiş saati başlangıçtan önce ya da eşitse (örn. 21:00–00:30), aralık ertesi güne taşar ve **başladığı günün** kuralına yazılır. Pazartesi 21:00–00:30 aralığı, Salı 00:00–00:30 arasını da kapsar.
- Aralık kapalıysa (veya hiç aralık yoksa) saat kısıtı uygulanmaz.
- Bitişik ya da çakışan aralıklar tek bir izin dönemi gibi değerlendirilir.
- Saat, bilgisayarın yerel saatine (Türkiye) göre hesaplanır.
- Web panelindeki saat aralığı düzenleyicisi bu alanları günceller; uygulama değişikliği bir sonraki senkronizasyonda uygular.

## Bilinen sınırlar

- Güvenli Mod (Safe Mode) ile açılan oturum kapsam dışıdır.
- Aynı bilgisayarda başka bir yönetici hesabı varsa kısıt bu hesapta aşılabilir; yönetici hesabı çocuğa verilmemelidir.
- Fiziksel erişim (bilgisayarı kapatıp açma, disk üzerinde müdahale vb.) kapsam dışıdır.
