using Umbraco.RedirectManager.Models;
using Umbraco.RedirectManager.Services;

namespace Umbraco.RedirectManager.Tests.Services;

public class MissedRequestClassifierTests
{
    [Theory]
    [InlineData("/wp-login.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/wp-admin/setup-config.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/.env", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/.git/config", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/some-random-scan.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/admin", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/phpmyadmin/index.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/xmlrpc.php", MissedRequestCategory.MaliciousScanner)]
    [InlineData("/assets/app.js", MissedRequestCategory.MissingAsset)]
    [InlineData("/styles/site.css", MissedRequestCategory.MissingAsset)]
    [InlineData("/assets/app.js.map", MissedRequestCategory.MissingAsset)]
    [InlineData("/images/logo.png", MissedRequestCategory.MissingAsset)]
    [InlineData("/fonts/icon.woff2", MissedRequestCategory.MissingAsset)]
    [InlineData("/old-product-page", MissedRequestCategory.Unclassified)]
    [InlineData("/blog/2019/my-post", MissedRequestCategory.Unclassified)]
    public void Classify_returns_expected_category(string path, MissedRequestCategory expected)
    {
        var result = MissedRequestClassifier.Classify(path);
        Assert.Equal(expected, result);
    }
}
