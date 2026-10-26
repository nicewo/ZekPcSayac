# ZekPcSayac Firebase kurulumu

Bu klasör, Firestore güvenlik kurallarını ve ilk kurulum notlarını içerir.

## Firestore veri modeli

- `admins/{firebaseAuthUid}`: Yönetim paneline erişebilen kullanıcılar. Belgeyi Firebase Console'dan elle oluşturun.
- `deviceConfig/{deviceId}`: Yalnızca yönetici tarafından değiştirilen günlük süre ve ek süre ayarları.
- `deviceState/{deviceId}`: Masaüstü uygulamasının kalan süre ve duraklatma gibi çalışma durumları.

Önerilen ilk cihaz kimliği: `zekeriyas-pc`.

### deviceConfig örnek belgesi

```json
{
  "deviceAuthUid": "MASAUSTU_ICIN_OLUSTURULAN_FIREBASE_AUTH_UID",
  "dailyLimitMinutes": 120,
  "bonusMinutes": 0,
  "configDateKey": "2026-10-26",
  "updatedAt": "Firestore Timestamp"
}
```

### deviceState örnek belgesi

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

Firestore'da `updatedAt` ve `lastSeenAt` için gerçek Firestore Timestamp alanlarını kullanın; örnek metinler yalnızca alan tipini açıklamak içindir.

## Firebase Console ilk kurulumu

1. Firebase Console'da bir proje oluşturun veya mevcut projeyi seçin.
2. Authentication > Sign-in method bölümünden **Email/Password** girişini etkinleştirin.
3. Authentication > Users bölümünden yönetici için bir kullanıcı oluşturun.
4. Aynı bölümden masaüstü cihazı için ayrı, sınırlı yetkili bir kullanıcı oluşturun. Yönetici parolasını masaüstü uygulamasına koymayın.
5. Firestore Database oluşturun.
6. Yönetici kullanıcısının UID'si ile `admins/{UID}` belgesi oluşturun (ör. `{"role":"admin" }`).
7. Masaüstü kullanıcısının UID'sini `deviceConfig/zekeriyas-pc` belgesindeki `deviceAuthUid` alanına yazın.
8. `deviceConfig/zekeriyas-pc` ve `deviceState/zekeriyas-pc` belgelerini oluşturun.
9. Firebase CLI kurulu bir geliştirme ortamında proje kökünden `firebase deploy --only firestore:rules` komutuyla `firebase/firestore.rules` kurallarını yayımlayın.

## Güvenlik notları

- Firebase Web API key, Firebase projesini tanımlar; tek başına yönetici yetkisi değildir. Güvenlik Firestore Rules ve Authentication ile uygulanır.
- Masaüstü uygulamasında yönetici hesabı kullanmayın. Ayrı cihaz hesabı yalnızca kendi cihaz belgesini okuyup çalışma durumu alanlarını güncelleyebilir.
- Firestore kuralları değiştirilmiş bir istemcinin kalan süreyi sahte biçimde raporlamasını bütünüyle engelleyemez. Bu uygulama ev içi kullanım kontrolü içindir, Windows güvenlik sınırı değildir.
- Bu aşamada WPF istemcisi henüz Firebase'e bağlanmıyor. Önce Firebase projesi ve bu kurallar kurulacak; ardından Auth/Firestore REST bağlantısı ile yerel önbellek ve senkronizasyon eklenecek.
