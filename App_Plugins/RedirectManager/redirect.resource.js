(function () {
    "use strict";

    function redirectResource($http) {
        var baseUrl = "/umbraco/api/redirectmanager/";

        return {
            getAll: function () {
                return $http.get(baseUrl + "getall");
            },
            getStats: function () {
                return $http.get(baseUrl + "stats");
            },
            get: function (id) {
                return $http.get(baseUrl + "get/" + id);
            },
            create: function (redirect) {
                return $http.post(baseUrl + "create", redirect);
            },
            update: function (id, redirect) {
                return $http.put(baseUrl + "update/" + id, redirect);
            },
            delete: function (id) {
                return $http.delete(baseUrl + "delete/" + id);
            },
            test: function (path) {
                return $http.get(baseUrl + "test", { params: { path: path } });
            },
            exportUrl: function () {
                return baseUrl + "export";
            },
            statsExportUrl: function () {
                return baseUrl + "stats/export";
            },
            importCsvContent: function (content) {
                // Send raw CSV content; backend will parse Request.Body
                return $http.post(baseUrl + "import", content, {
                    headers: { "Content-Type": "text/plain; charset=utf-8" }
                });
            },
            getMissed: function () {
                return $http.get(baseUrl + "missed");
            },
            dismissMissed: function (id) {
                return $http.delete(baseUrl + "missed/" + id);
            }
        };
    }

    angular.module("umbraco.resources").factory("redirectResource", ["$http", redirectResource]);
})();
