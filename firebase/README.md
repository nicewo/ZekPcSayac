# ZekPcSayac Firebase kurulumu

Firebase Console'da oluşturulan `zekpcsayac` projesi ve bu repodaki başlangıç dosyaları için kurulum rehberi.

## Veri modeli

- `admins/{firebaseAuthUid}`: Yönetim paneline erişebilen kullanıcılar. Bu belge Firebase Console'dan elle oluşturulur.
- `deviceConfig/zekeriyas-pc`: Günlük hak, o güne ait ek süre ve masaüstü hesabının UID'si.
- `deviceState/zekeriyas-pc`: Masaüstü uygulamasının bildirdiği kalan süre ve duraklatma durumu.

### deviceConfig örneği

```json
{
  "deviceAuthUid": "MASAUSTU_FIREBASE_AUTH_UID",
  "dailyLimitMinutes": 120,
  "bonusMinutes": 0,
  "bonusDateKey": "2026-10-26"
}
```

`bonusDateKey`, ek sürenin hangi güne ait olduğunu belirtir. Ek süre başka güne taşınmamalıdır.

### deviceState örneği

```json
{
  "remainingSeconds": 7200,
  "isPaused": false,
  "dateKey": "2026-10-26",
  "lastSeenAt": "Firestore Timestamp",
  "updatedAt": "Firestore Timestamp",
  "clientVersion": "0.1.0"
}
```

Tarih alanları `yyyy-MM-dd` biçiminde yerel tarih metnidir. `lastSeenAt` ve `updatedAt` gerçek Firestore Timestamp değerleri olmalıdır.

## Firebase Console kurulumu

1. Proje: `zekpcsayac`.
2. Authentication > Sign-in method bölümünde **Email/Password** etkin olmalı.
3. Authentication > Users bölümünde bir yönetici kullanıcı oluştur.
4. Yönetici UID'sini kopyala. Firestore Data ekranında `admins` koleksiyonu ve UID ile aynı belgeyi oluştur; örneğin `{"role":"admin"}`.
5. Authentication > Users bölümünde masaüstü için ayrı bir kullanıcı oluştur. Yönetici parolasını masaüstü uygulamasına koyma.
6. Masaüstü kullanıcı UID'sini kopyala. Paneldeki “Masaüstü cihaz hesabı” alanından kaydedebilirsin.
7. Yönetici hesabıyla web paneline giriş yapmadan önce Firestore kurallarını yayımla. Ardından panelden günlük süre ve ek süre ayarlanabilir.

## Kuralları ve web panelini yayımlama

Bilgisayarında Firebase CLI yoksa Node.js LTS kurduktan sonra terminalde:

```powershell
npm install -g firebase-tools
firebase login
```

Repo klasöründe şu komutları çalıştır:

```powershell
firebase use --add
```

Listeden `zekpcsayac` projesini seç ve bir alias (ör. `default`) ver. Ardından:

```powershell
firebase deploy --only firestore:rules,hosting
```

Bu komut `firebase/firestore.rules` kurallarını ve `web-admin` klasöründeki telefon uyumlu yönetim panelini yayımlar. Deploy çıktısında Hosting URL'si görünür.

## Güvenlik ve mevcut kapsam

- Firebase Web API key, web istemcisinde bulunması normal olan proje tanımlayıcısıdır; yetkiyi Authentication ve Firestore Rules belirler.
- Yönetici hesabını masaüstü uygulamasında kullanma. Masaüstüne ayrı Firebase kullanıcısı tanımla.
- Admin erişimi, yalnızca `admins/{UID}` belgesi bulunan kullanıcıya verilir. Bu belgeyi Console üzerinden elle oluştur; kurallar istemciden bu belgeyi oluşturmayı engeller.
- Firestore kuralları değiştirilmiş bir istemcinin yerel kalan süreyi sahte raporlamasını bütünüyle engelleyemez. Bu ev içi kullanım kontrolü uygulamasıdır, Windows güvenlik sınırı değildir.
- Web paneli başlangıç sürümüdür. WPF istemcisi henüz Firestore'a bağlanmıyor; gerçek senkronizasyon ve çevrimdışı davranış sonraki adımda eklenecek.
