# WinDash

Windows için cam (glass) görünümlü, özelleştirilebilir kişisel gösterge paneli. İkinci bir ekranda tam ekran
ya da tek ekranda normal bir pencere olarak çalışır. C# / .NET 8 / WPF.

WinDash **hiçbir bilgisayara, markaya veya kişiye bağlı değildir**: adını Windows hesabından, konumunu Windows konum
hizmetinden, cihazlarını Windows'un aygıt listesinden, uygulamalarını Başlat menüsünden kendisi bulur.
Kurduktan sonra hiçbir ayar yapmadan çalışır; `appsettings.json` yalnızca varsayılanları değiştirmek isteyenler içindir.

Arka planda masaüstü duvar kâğıdının bulanık kopyası, üzerinde yarı saydam cam kartlar durur (kart altı ek bulanıklık
+ %180 doygunluk + gölge, açılışta tek bir bitmap'e işlenir; çalışırken maliyeti yoktur). Manrope ve JetBrains Mono
yazı tipleri uygulamaya gömülüdür. Tasarım alanı 1920x1080'dir; farklı çözünürlüklerde oranını koruyarak ölçeklenir.

## Kartlar

| Tür (`layout.json`) | Ne gösterir | Kaynak |
|---|---|---|
| `clock` | Selamlama (Windows'taki adınla), saat, tarih, hafta numarası, şehir ve saat dilimi | Sistem saati, Windows hesabı |
| `weather` | Anlık durum, hissedilen, nem, rüzgâr, 5 günlük tahmin | Open-Meteo (anahtar gerekmez), konum otomatik |
| `apps` | Sık kullanılan uygulamalar; tıkla-aç, kartta ekle / kaldır / sırala | Başlat menüsü "Tüm uygulamalar" (Store dahil) |
| `media` | Çalan şarkı / video, kapak, ilerleme, oynat/duraklat/ileri/geri/karıştır/tekrarla | Windows medya oturumu (Spotify, tarayıcı, medya oynatıcı…) |
| `news` | Kategori sekmeli son haberler, tıklayınca tarayıcıda açılır | RSS / Atom |
| `devices` | Klavye, fare, kulaklık, mikrofon, hoparlör; pil ve bağlantı durumu; aygıt seçimi | HID + Core Audio + Bluetooth |
| `window` | Küçült / büyüt / tam ekran / kapat; tam ekranda Masaüstü / Başlat / Ara | Ana pencere |
| `controls` | Wi-Fi, Bluetooth aç/kapat, Gece Işığı aç/kapat, ekran parlaklığı, ses seviyesi | Windows API'leri, DDC/CI |
| `system` | CPU, RAM, GPU, disk (`"Variant": "compact"` → 2x2) | Windows performans sayaçları |
| `calendar` | Ay görünümü, bugünün etkinlikleri (varsayılan yerleşimde kapalı) | `.ics` adresi (Google Takvim vb.) |
| `network` | Yalnızca Wi-Fi bilgisi (isteğe bağlı; `controls` içinde zaten var) | Windows ağ API'leri |

## Neyi kendisi bulur?

| Bilgi | Nereden | Elle değiştirmek için |
|---|---|---|
| Selamlamadaki ad | Windows hesabının görünen adının ilk kelimesi (yoksa kullanıcı adı) | `UserName` |
| Konum (hava durumu, şehir) | 1. Windows konum hizmeti → 2. IP'den şehir düzeyinde tahmin. Günde bir yenilenir | `Weather.Latitude` / `Longitude`, `Clock.LocationLabel` |
| Monitör | İkinci ekran varsa o, yoksa ana ekran | `Display.MonitorDeviceName` |
| Açılış modu | İlk açılışta: 2+ ekran varsa tam ekran, tek ekranda pencere. Sonra son seçilen mod | Pencere kartı |
| Uygulamalar | Varsayılan listeden (Chrome, Edge, Firefox, Spotify, WhatsApp, Discord, Steam, VS Code, Office…) **bilgisayarda kurulu olanlar** | Kartta *Ekle*, `Apps.Defaults` |
| Klavye / fare | Bağlı HID aygıtları; pili okunabilen ve kablosuz olan öne çıkar | Satıra tıkla → seç |
| Kulaklık | Windows'un "Kulaklık / Mikrofonlu kulaklık" dediği ses aygıtı (sanal aygıtlar hariç) | Satıra tıkla → seç |
| Mikrofon / hoparlör | Windows'ta varsayılan olanlar | Satıra tıkla → Windows varsayılanını değiştirir |
| Diskler | Windows'un kurulu olduğu sürücü | `SystemMonitor.Drives` |

## Gereksinimler

- Windows 10 2004 (19041) veya Windows 11, x64
- Derlemek için [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (veya daha yenisi). Kurulan MSI .NET
  çalışma zamanını içinde taşır; hedef bilgisayara ayrıca bir şey kurmak gerekmez.

## Kurulum paketi (MSI)

**`build-installer.cmd`**'ye çift tıkla (veya `.\build-installer.ps1 -Version 1.1.0`).
`artifacts\WinDash-Setup-1.1.0.msi` oluşur. WiX Toolset NuGet'ten kendiliğinden iner.

MSI'a çift tıklayınca açılan pencere duruma göre değişir:

| Durum | Pencere |
|---|---|
| WinDash kurulu değil | "WinDash 1.1.0 kurulacak" · **Kur** / İptal |
| Eski bir sürüm kurulu | "Bilgisayarınızda WinDash 1.0.0 yüklü. Yeni sürüme (1.1.0) güncellemek istiyor musunuz?" · **Güncelle** / İptal |
| Aynı sürüm (yeniden derlenmiş paket) kurulu | "Aynı sürümü yeniden kurmak istiyor musunuz?" · **Güncelle** / İptal |
| Aynı MSI tekrar açıldı | **Onar** / **Kaldır** / İptal |
| Daha yeni bir sürüm kurulu | Hata verir ve durur (eski sürüme dönüş yok) |

Kur / Güncelle penceresindeki **"Windows açılınca WinDash'i başlat"** kutusu işaretli gelir.

Kurulum:

- `%LOCALAPPDATA%\Programs\WinDash` klasörüne kurar (yönetici izni gerekmez),
- Başlat menüsüne **WinDash** kısayolu ekler,
- Windows açılışında başlatır (kutu işaretliyse) ve kurulum bitince uygulamayı açar,
- Güncellemede açık olan WinDash'i kapatır, eski sürümü kaldırıp yenisini kurar ve yeniden açar.
  Ayarlar, kart seçimleri ve uygulama listesi `%LOCALAPPDATA%\WinDash` içinde olduğu için **korunur**
  (kaldırmada da silinmez).

| Komut | Ne yapar |
|---|---|
| `msiexec /i WinDash-Setup-1.1.0.msi AUTOSTART=0` | Windows ile başlatmadan kurar |
| `msiexec /i WinDash-Setup-1.1.0.msi /qn` | Sessiz kurulum / güncelleme (soru sormaz) |
| `build-installer.ps1 -FrameworkDependent` | Daha küçük MSI (hedef bilgisayarda .NET 8 Desktop Runtime gerekir) |

**Yeni sürüm yayınlarken** sürüm numarasını artır (`-Version 1.2.0`); kurulum bunu güncelleme olarak tanır.

Windows ile başlatma sonradan **panelde sağ tık > Windows ile başlat** ile açılıp kapatılabilir; Görev Yöneticisi >
Başlangıç uygulamaları'nda "WinDash" olarak görünür. Açılışta birkaç saniye bekler, ikinci ekran geç gelirse
30 saniyeye kadar onu bekler ve odağı çalmadan açılır.

## Pencere kartı ve modlar

Cihazlar kartının üstündeki cam **Pencere** kartı. Her simgenin üzerine gelince ne yaptığını anlatan bir ipucu çıkar.

| Düğme | Ne yapar |
|---|---|
| Küçült | Görev çubuğuna küçültür |
| Büyüt / Geri al | Monitörün görev çubuğu hariç alanını kaplar ↔ pencere |
| Tam ekran (F11) | Monitörün tamamını kaplar ↔ önceki mod |
| Kapat | WinDash'i kapatır (Ctrl+Q) |
| **Masaüstü** · **Başlat** · **Ara** | *Yalnızca tam ekranda:* masaüstünü göster, Başlat menüsünü aç, Windows aramasını aç (Win+S) |

Pencere modunda kartı tutup sürükleyerek pencereyi taşıyabilir, kenarlardan boyutlandırabilirsin. Karta çift tıklamak
büyütür. Seçilen mod ve pencere konumu hatırlanır.

### Kısayollar

- **F5** – ayarları ve yerleşimi yeniden yükler
- **F11** – tam ekran
- **Ctrl+Q** – çıkış
- **Sağ tık** (boş alana) – Yenile / Ayar klasörünü aç / Günlüğü aç / Windows ile başlat / Çıkış

## Uygulamalar kartı

- **Tıkla** → uygulama açılır.
- **Ekle** → kurulu uygulamalar arasında arama (Microsoft Store uygulamaları dahil) veya *Gözat…* ile bir .exe / kısayol.
- **Sağ tık** → sola / sağa taşı, konumunu seç, kaldır.
- İlk açılışta `Apps.Defaults` listesinden **yalnızca bilgisayarda kurulu olanlar** gösterilir (en fazla iki satır).
  Değişiklikler `state.json`'a kaydedilir; *Ekle > Varsayılanlara dön* listeyi yeniden tarar.

## Kontrol merkezi

| Öğe | Kullanım | Not |
|---|---|---|
| Wi-Fi | Bağlı ağ ve sinyal | Windows 11 24H2+: konum izni gerekir (aşağıya bak) |
| Bluetooth | Tıkla: aç / kapat. Altında bağlı cihaz sayısı, üzerine gelince adları | Bluetooth donanımı yoksa soluk |
| Gece Işığı | Tıkla: aç / kapat. Sağ tık: Ayarlar > Gece ışığı | Aşağıdaki nota bak |
| Parlaklık | Sürükle veya fare tekerleği | Harici monitörde DDC/CI açık olmalı |
| Ses | Sürükle veya tekerlek; **ikona tıkla: sessize al** | Windows varsayılan çıkışı |

**Parlaklık:** Harici monitörlerin parlaklığı DDC/CI ile değiştirilir (`Controls.BrightnessTarget`:
`"all"`, `"primary"` veya `\\.\DISPLAY2` gibi bir ad). Monitörün kendi menüsünde DDC/CI açık olmalı.
Desteklemeyen ekranlarda kaydırıcı soluk görünür ve "DDC/CI yok" yazar.

**Gece Işığı:** Microsoft bunun için bir API sunmuyor; Ayarlar uygulamasının kullandığı kayıt defteri bloğu değiştirilir.
Uygulama bloğu yalnızca bilinen biçimdeyse değiştirir ve ilk değişiklikten önce yedeğini alır. Biçim tanınmazsa hiçbir
şey yazmaz ve Ayarlar sayfasını açar. İşe yaramazsa `Controls.NightLightMode` değerini `"settings"` yap.

## Cihazlar kartı

Her satır bir **yuvadır**: klavye, fare, kulaklık, mikrofon, hoparlör. Aygıtlar **Windows'tan otomatik algılanır**;
takıp çıkardığında kart kendiliğinden güncellenir. Alt yazıda bağlantı türü (Bluetooth / Kablosuz alıcı / USB) ya da
ses aygıtı türü (ör. *Optik (S/PDIF)*) görünür.

- **Satıra tıkla** → o yuva için bağlı aygıtların listesi açılır.
  - Klavye / Fare / Kulaklık: seçimin kaydedilir; "Otomatik" kurala döner.
  - **Mikrofon / Hoparlör: her zaman Windows'ta seçili aygıtı gösterir.** Listeden seçim Windows'un varsayılan
    aygıtını değiştirir (Ses ayarlarındaki "Varsayılan yap" ile aynı).
- **İkona tıkla** → hızlı işlem: mikrofon ve hoparlörde sessize al, kulaklıkta onu varsayılan çıkış yap.
- Optik veya analog kabloyla bağlanan bir hoparlörün adı bilgisayara ulaşmaz ("Realtek Digital Output" gibi görünür);
  okunur bir ad vermek için `Devices.AudioAliases` kullan:

  ```json
  "AudioAliases": [ { "Contains": "Realtek Digital Output", "Name": "Masa hoparlörü", "Subtitle": "Optik" } ]
  ```

### Pil bilgisi

| Cihaz | Pil |
|---|---|
| **Bluetooth** ile bağlı her cihaz (klavye, fare, kulaklık…) | Windows'un bildirdiği yüzde; ek bir şey gerekmez |
| Standart HID pil raporu gönderen kablosuz cihazlar | Okunur |
| Diğer 2.4 GHz alıcılı / USB cihazlar | Üreticiler bunu genelde kendi yazılımlarına özel, belgelenmemiş bir protokolle gönderir; yalnızca bağlantı durumu görünür |

Belgelenmemiş protokolleri bilinen birkaç cihaz için ek destek vardır:

- SteelSeries Aerox 3 Wireless ([rivalcfg](https://github.com/flozz/rivalcfg) protokolü; fare uykudaysa "Uykuda")
- Keychron Q/K HE serisi, topluluk firmware'iyle 2.4 GHz'de (raw HID `0xA4` raporu,
  `Devices.KeychronBatteryListener`). Standart firmware pili 2.4 GHz'de hiç göndermez; Bluetooth modunda Windows
  üzerinden zaten görünür.

**Tanılama:** Cihazlar kartında bir satıra **sağ tık > Aygıt tanılaması (15 sn)**. 15 saniye boyunca o aygıtın tüm HID
arayüzlerini, standart pil alanı olup olmadığını, Bluetooth pil bilgisini ve gelen ham raporları `log.txt`'ye
`[HID TANI]` başlığıyla yazar ve günlüğü açar. Yeni bir cihaz için destek eklemek isteyenlere başlangıç noktasıdır.

## Ayarlar

Ayarlar **`%LOCALAPPDATA%\WinDash`** klasöründedir (panelde sağ tık > *Ayar klasörünü aç*). İlk açılışta
kurulum klasöründeki varsayılanlar buraya kopyalanır; düzenledikten sonra **F5** ile yeniden yükle.

- **`appsettings.json`** – ad, vurgu rengi, kart saydamlığı, konum, haber kaynakları, uygulamalar, cihaz yuvaları…
  Boş bırakılan her değer otomatik algılanır.
- **`layout.json`** – 12 sütun x 12 satırlık ızgarada hangi kart nerede, ne kadar yer kaplıyor.
  Yeni sürümle yerleşim değiştiyse eskisi `layout.backup.json` olarak saklanır.
- `state.json` – kartta yaptığın seçimler (uygulama listesi, cihazlar, pencere modu, bulunan konum); `log.txt` – günlük.

Varsayılanlara dönmek için ilgili dosyayı sil; bir sonraki açılışta yeniden kopyalanır.

### Takvim

Google Takvim → Ayarlar → (takvimin) → *Takvimi entegre et* → **iCal biçimindeki gizli adres** bağlantısını
`Calendar.IcsUrls` listesine ekle ve `layout.json`'a bir `calendar` kartı yerleştir.

## Gizlilik

WinDash hesap, anahtar veya kayıt istemez ve hiçbir veriyi bir sunucuda toplamaz. Dışarıya yalnızca şu istekler gider:
hava durumu (Open-Meteo), konumdan şehir adı (BigDataCloud) veya Windows konumu kapalıysa IP'den konum (ipwho.is),
haber kaynakları ve takvim adresi. Konumu elle yazarsan (`Weather.Latitude` / `Longitude`) konum servislerine hiç
istek gitmez.

## Bilinen kısıtlar

- **Windows 11 24H2+:** Wi-Fi ağ adı için *Ayarlar → Gizlilik ve güvenlik → Konum* açık olmalı.
- Mikrofon seviye göstergesi, Windows değeri yalnızca bir uygulama mikrofonu kullanırken güncellediği için
  diğer zamanlarda boş görünebilir.
- Kendi protokolünü kullanan 2.4 GHz cihazların çoğunda pil okunamaz (yukarıya bak).

## Geliştirme

```powershell
dotnet run --project src/PcDashboard
```

Elle yayınlarsan `WinDash.exe`, `appsettings.json`, `layout.json` (ve `Fonts\OFL-*.txt` lisansları) aynı klasörde olmalı.

Yeni kaynak kodunu **boş bir klasöre** aç; eski sürümden kalan, yeni sürümde silinmiş dosyalar derleme hatasına yol açar.
`System.IO` ve `System.Net.Http` projede genel (global) olarak ekli; dil sürümü C# 12'ye sabit (daha yeni SDK'larla da
aynı şekilde derlenir).

### Yeni kart eklemek

1. `Widgets/<Ad>/` altında bir `UserControl` ve `WidgetViewModel`'den türeyen bir ViewModel oluştur.
2. `StartAsync` içinde `Every(...)` / `EveryAsync(...)` ile yenileme aralıklarını tanımla.
3. `Core/WidgetFactory.cs` içine bir satır ekle (kartın iç boşluğuyla birlikte) ve `layout.json`'a yerleştir.

### Proje yapısı

```
build-installer.cmd/.ps1        MSI paketini üretir (artifacts\WinDash-Setup-<sürüm>.msi)
installer/WinDash.Installer/
  Package.wxs                   Kurulum içeriği (dosyalar, kısayol, başlangıç, yükseltme)
  WinDashUI.wxs                 Kur / Güncelle / Onar / Kaldır pencereleri
  WinDash.tr-TR.wxl             Kurulum metinleri
src/PcDashboard/
  App.xaml(.cs)           Başlangıç, tema, tek örnek kontrolü
  MainWindow.xaml(.cs)    Pencere, arka plan, ızgara, kısayollar
  MainWindow.Chrome.cs    Pencere modları (tam ekran / büyütülmüş / pencere)
  Core/                   Ayarlar, durum (state.json), günlük, widget temel sınıfı ve fabrikası
  Services/               Monitör, duvar kâğıdı, konum, ses, parlaklık, gece ışığı, bluetooth, HID, pil
  Interop/                Win32 P/Invoke tanımları
  Controls/               İkon, harf aralıklı metin, hap kaydırıcı, yerleşim panelleri
  Themes/                 Cam stilleri ve ikon geometrileri
  Assets/                 Uygulama ikonu (WinDash.ico)
  Fonts/                  Manrope, JetBrains Mono (SIL OFL)
  Widgets/                Her kart kendi klasöründe (görünüm + ViewModel)
```

## Lisanslar

Manrope ve JetBrains Mono yazı tipleri SIL Open Font License 1.1 ile dağıtılır (`Fonts\OFL-*.txt`).
#
