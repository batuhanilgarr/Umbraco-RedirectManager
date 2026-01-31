# Redirect Manager – Geliştirme ve Özellik Fikirleri

Mevcut yapı: **301/302/404/410**, **exact + regex**, **CRUD + bulk**, **CSV import/export API**, **backoffice dashboard**, **NuGet paketi**.

Aşağıda hem **teknik iyileştirmeler** hem de **yeni özellik** fikirleri var; istediğiniz sırayla ekleyebilirsiniz.

---

## Teknik / Geliştirme İyileştirmeleri

### 1. **appsettings ile yapılandırma**
- **Skip paths:** Şu an middleware içinde sabit (`/umbraco`, `/api`, …). Bunları `appsettings.json` (veya `appsettings.RedirectManager.json`) içine alıp okumak.
- **Regex cache süresi:** `RedirectService` içindeki 30 saniye cache süresini config’ten okumak.
- **Query string:** Redirect sırasında query string’i koruma seçeneği (global veya kural bazlı) – aşağıda “Query string koruma” ile birlikte düşünülebilir.

### 2. **Obsolete API controller**
- `UmbracoApiController` obsolete; Umbraco 17+ için `BackOfficeController` veya **Minimal API** ile `/umbraco/backoffice/...` veya ayrı bir backoffice API endpoint’i kullanmak.
- Mevcut route’lar (`/umbraco/api/redirectmanager/...`) korunacak şekilde yeni controller’a taşınabilir.

### 3. **Unit / entegrasyon testleri**
- `RedirectService`: Create, Update, Delete, GetByOldUrl, regex cache.
- `RedirectMiddleware`: Skip paths, exact redirect, regex redirect, 404/410.
- API: Create, Update, validation, duplicate check, CSV import/export (birkaç satır).

### 4. **Health check**
- Basit bir endpoint (örn. `/umbraco/api/redirectmanager/health`) veya Umbraco health check’e “redirect table exists / DB erişilebilir” gibi bir kontrol eklemek.

### 5. **Logging ve izlenebilirlik**
- Redirect uygulandığında (middleware) `Information` veya `Debug` log ile: OldUrl → NewUrl, StatusCode.
- Hata durumlarında (regex timeout, exception) mevcut log’ları koruyup gerekirse biraz daha yapılandırılabilir hale getirmek.

---

## Yeni Özellik Fikirleri

### 1. **Query string koruma (Preserve query string)**
- **Ne:** 301/302’de hedef URL’e, istekteki query string’i eklemek (örn. `/eski?utm_source=google` → `/yeni?utm_source=google`).
- **Nasıl:** `RedirectEntry`’e `PreserveQueryString` (bool) alanı; middleware’de Location header’ı oluştururken `context.Request.QueryString` eklenir.
- **Zorluk:** Düşük.

### 2. **Hit sayacı (Redirect hit / tıklanma)**
- **Ne:** Her redirect kuralı için “kaç kez kullanıldı” sayacı; backoffice’te listeleme ve basit grafik.
- **Nasıl:** Tabloya `HitCount` (bigint) + `LastHitDate`; middleware’de eşleşen kural için artırma (async/fire-and-forget veya background job ile DB’ye yazma).
- **Zorluk:** Orta (performans: her redirect’te yazma yerine cache + periyodik flush veya queue kullanılabilir).

### 3. **Geçerlilik tarihleri (Valid from / until)**
- **Ne:** “Bu tarihten sonra aktif” / “Bu tarihe kadar aktif” (kampanya, geçici sayfa taşıma).
- **Nasıl:** `RedirectEntry`’e `ValidFrom`, `ValidUntil` (nullable DateTime); middleware ve API’de filtre.
- **Zorluk:** Düşük.

### 4. **Basit wildcard (*) eşleşme**
- **Ne:** Regex bilmeyen kullanıcılar için `/blog/*` → `/haberler/*` gibi tek wildcard.
- **Nasıl:** `OldUrl` içinde `*` varsa ve `IsRegex` false ise, `*` → `(.*)` dönüşümü ile tek bir regex üretip mevcut regex motorunda kullanmak; `NewUrl`’de `*` → `$1` gibi.
- **Zorluk:** Düşük–orta.

### 5. **Backoffice’te Export / Import UI**
- **Ne:** API’de zaten var; backoffice’te “Export CSV” ve “Import CSV” butonları + import sonrası “X created, Y updated, Z skipped” mesajı.
- **Nasıl:** Dashboard’a butonlar; `redirectResource.export()` (GET) ve `redirectResource.import(file)` (POST) ile mevcut API’yi kullanmak.
- **Zorluk:** Düşük.

### 6. **Backoffice’te “Test URL” alanı**
- **Ne:** Bir URL girip “Bu URL hangi kurala düşer, nereye gider?” görmek.
- **Nasıl:** Mevcut `GET .../test?path=...` API’yi kullanarak dashboard’a küçük bir “Test URL” kutusu + sonuç (matched rule, computed NewUrl, status code).
- **Zorluk:** Düşük.

### 7. **Backoffice’te bulk seçim ve toplu işlem**
- **Ne:** Tabloda checkbox ile çoklu seçim; “Seçilenleri sil”, “Seçilenleri aktif/pasif yap” (API zaten var).
- **Nasıl:** `ng-repeat` ile satırlara checkbox; “Bulk delete” / “Bulk activate” / “Bulk deactivate” butonları ve mevcut bulk API’ler.
- **Zorluk:** Düşük.

### 8. **Filtreleme ve arama (UI)**
- **Ne:** Backoffice listesinde “Ara”, “Status code”, “Aktif/Pasif”, “Regex/Exact” filtreleri.
- **Nasıl:** API’deki `GetAllFiltered(q, statusCode, isActive, isRegex)` kullanılır; dashboard’a filtre alanları eklenir.
- **Zorluk:** Düşük.

### 9. **Audit alanları (CreatedBy / ModifiedBy)**
- **Ne:** Kim, ne zaman ekledi/güncelledi (SEO/uyumluluk raporları için).
- **Nasıl:** Tabloya `CreatedBy`, `ModifiedBy` (int, Umbraco user id) ve isteğe bağlı `ModifiedDate`; API’de mevcut backoffice kullanıcısını set etmek.
- **Zorluk:** Düşük.

### 10. **Trailing slash normalizasyonu**
- **Ne:** `/sayfa` ile `/sayfa/` aynı kurala düşsün; veya “trailing slash ekle/kaldır” yönlendirmesi.
- **Nasıl:** Middleware’de path’i normalize edip önce bu path ile lookup; bulunamazsa `/path` ↔ `/path/` ile tekrar dene (ve isteğe bağlı 301 ile normalize et).
- **Zorluk:** Düşük.

### 11. **Çakışma uyarısı (Duplicate / overlap)**
- **Ne:** Yeni kural eklerken “Bu path zaten şu kuralda var” veya “Bu regex, şu exact kuralı kapsıyor” uyarısı.
- **Nasıl:** Create/Update API’de mevcut `GetByOldUrlAndIsRegex` dışında, regex kurallarının birbirini veya exact’i kapsayıp kapsamadığını basit kontrollerle (ör. test path’leri) uyarı olarak dönmek; backoffice’te gösterilmek.
- **Zorluk:** Orta.

### 12. **Dashboard istatistikleri**
- **Ne:** Toplam redirect sayısı, aktif/pasif, son eklenen 5–10 kural, (hit sayacı eklendiyse) en çok kullanılan 5–10 kural.
- **Nasıl:** API’de `GET .../stats` (count’lar, son eklenenler); isteğe bağlı hit count ile “top redirects”.
- **Zorluk:** Düşük (hit yoksa); hit ile orta.

### 13. **Çoklu site / kültür (multi-site)**
- **Ne:** Farklı Umbraco siteleri veya dillerde farklı redirect listesi (domain veya kültüre göre).
- **Nasıl:** Tabloya `Culture` veya `SiteId` (nullable); middleware’de request’e göre filtre. Umbraco’nun site binding’i ile entegre düşünmek.
- **Zorluk:** Orta–yüksek.

### 14. **Rate limiting (isteğe bağlı)**
- **Ne:** Aynı IP’den çok sayıda redirect isteğinde 429 veya log-only.
- **Nasıl:** Middleware’de IP bazlı sayaç veya `AspNetCoreRateLimit` gibi paket; sadece redirect path’lerine uygulanabilir.
- **Zorluk:** Orta.

---

## Öncelik Önerisi (Hızlı kazanımlar)

| Sıra | Özellik / İyileştirme | Neden |
|------|------------------------|-------|
| 1 | Backoffice: Export / Import UI | API hazır; kullanıcı tek tıkla CSV alır/yükler. |
| 2 | Backoffice: Test URL alanı | API hazır; kural testi çok işe yarar. |
| 3 | Query string koruma | SEO ve kampanya linkleri için sık istenir. |
| 4 | appsettings ile skip paths + cache süresi | Deploy ortamına göre esnek yapı. |
| 5 | Backoffice: Filtreleme + bulk seçim | Çok sayıda kural varken yönetimi kolaylaştırır. |
| 6 | Geçerlilik tarihleri (Valid from/until) | Kampanya ve geçici taşımalar için mantıklı. |
| 7 | Hit sayacı + dashboard istatistikleri | Hangi kuralların kullanıldığını görmek için. |

İsterseniz bir sonraki adımda seçtiğiniz bir özellik için (örn. “Query string koruma” veya “Backoffice Export/Import UI”) doğrudan kod taslağı ve değişiklik listesi çıkarabilirim.
