redirects-from-webconfig.csv — web.config.prod içindeki yönlendirmelerin plugin import formatında CSV'si.

Kullanım:
1. Backoffice → Settings → Redirect Manager
2. Import CSV ile bu dosyayı seçin (API: POST /umbraco/api/redirectmanager/import, form file)

Not — Query string'li kurallar (raporlar.aspx?type=15 vb.):
Plugin şu an sadece path (URL yolu) ile eşleşiyor; query string dikkate alınmıyor. Bu yüzden
/raporlar.aspx?type=15 gibi satırlar import edilir ama istek /raporlar.aspx?type=15 geldiğinde
lookup path /raporlar.aspx ile yapıldığı için bu kayıt bulunmayabilir. Query string desteği
eklenene kadar bu kurallar beklendiği gibi çalışmayabilir; path-only kurallar (örn. /ak-yatirim-dunyasi.aspx)
doğrudan çalışır.

İstisna: Eğer ileride middleware path+query ile lookup yaparsa, bu CSV'deki tüm satırlar
aynen kullanılabilir.
