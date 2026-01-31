# NuGet ile Dağıtım ve Kurulum

İstediğiniz projede `dotnet add package 8Bitiz.RedirectManager` ile kurulum yapabilirsiniz. Aşağıda **Docker ile kendi NuGet sunucunuzu** kurma (önerilen) ve diğer seçenekler anlatılıyor.

---

## Önerilen: Docker ile kendi NuGet sunucusu

Kendi NuGet sunucunuzu Docker’da çalıştırıp plugini oraya atar, sonra her projede `dotnet add package` ile kurarsınız.

### 1. NuGet sunucusunu (BaGet) başlatın

Bu repo kökündeyken:

```bash
docker compose -f docker/docker-compose.yml up -d
```

- Sunucu: **http://localhost:5555**
- Feed URL (projelerde kullanacağınız): **http://localhost:5555/v3/index.json**
- Paketleri tarayıcıda görmek için: http://localhost:5555

### 2. Plugini paketleyip sunucuya gönderin

**macOS/Linux:**
```bash
./scripts/push-to-feed.sh
```

**Windows (PowerShell):**
```powershell
.\scripts\push-to-feed.ps1
```

Bu script paketi build edip BaGet’e push eder. Sunucu başka makinedeyse:

```bash
export BAGET_URL="http://SUNUCU-IP:5555/v3/index.json"
export BAGET_API_KEY="NUGET-SERVER-API-KEY"   # docker/baget.env ile aynı
./scripts/push-to-feed.sh
```

### 3. Kurulum yapacağınız projede feed’i ekleyin

O projenin **solution klasörüne** (`.sln` dosyasının olduğu yere) `nuget.config` koyun. İçeriği `nuget.config.example` ile aynı olabilir; Docker sunucu aynı makinedeyse:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="RedirectManagerNuGet" value="http://localhost:5555/v3/index.json" />
  </packageSources>
</configuration>
```

Sunucu başka makinedeyse `localhost` yerine o makinenin IP/host adresini yazın. HTTP kullandığınız için `allowInsecureConnections="true"` ekleyin (örnek: `nuget.config.example`).

### 4. Projeye paketi ekleyin

```bash
cd /path/to/your-umbraco-project
dotnet add package 8Bitiz.RedirectManager
dotnet build
```

Bundan sonra istediğiniz projede aynı şekilde `dotnet add package 8Bitiz.RedirectManager` kullanabilirsiniz.

**Paket adını değiştirmek:** Kurulum komutunda farklı bir isim (örn. `Umbraco.Engage`) kullanmak isterseniz, bu projenin `Umbraco.RedirectManager.csproj` dosyasında `<PackageId>` değerini değiştirip paketi yeniden build edip push etmeniz yeterli. O zaman `dotnet add package Umbraco.Engage` çalışır.

---

## Diğer seçenekler

### Seçenek A: Yerel klasör feed (sunucu yok)

1. Bu repoda: `dotnet build` → paket `bin/Debug/net10.0/` altında oluşur.
2. Hedef projenin solution klasörüne `nuget.config` ekleyin; feed olarak bu klasörün **absolute path**’ini verin (örnek: `nuget.config.example` içindeki “Seçenek C”).
3. Hedef projede: `dotnet add package 8Bitiz.RedirectManager`

### Seçenek B: nuget.org (herkese açık)

1. nuget.org hesabı + API Key.
2. `dotnet pack -c Release` ve `dotnet nuget push ... --source https://api.nuget.org/v3/index.json`
3. Hedef projede ek feed gerekmez; `dotnet add package 8Bitiz.RedirectManager` yeterli.

### Seçenek C: BaGet’i elle çalıştırma (Docker Compose kullanmadan)

```bash
docker run -d -p 5555:80 --name nuget-server --env-file docker/baget.env \
  -v "$(pwd)/baget-data:/var/baget" loicsharma/baget:latest
```

Feed URL: `http://localhost:5555/v3/index.json`. Push için `scripts/push-to-feed.sh` veya `push-to-feed.ps1` kullanabilirsiniz (BAGET_URL’i buna göre ayarlayın).

---

## Özet

| Yöntem                | Ne zaman kullanılır                    |
|-----------------------|----------------------------------------|
| **Docker BaGet**      | Kendi sunucunuz, tüm projelerde `dotnet add package` (önerilen) |
| Yerel klasör feed     | Tek makine, sunucu istemiyorsanız      |
| nuget.org             | Paketi herkese açık yayınlamak         |

Docker sunucuyu durdurmak için: `docker compose -f docker/docker-compose.yml down`
