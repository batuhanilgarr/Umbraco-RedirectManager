(function () {
    "use strict";

    function redirectResource($http) {
        var baseUrl = "/umbraco/api/redirectmanager/";

        return {
            getAll: function () {
                return $http.get(baseUrl + "getall");
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
            importCsv: function (file) {
                var formData = new FormData();
                formData.append("file", file);
                return $http.post(baseUrl + "import", formData, {
                    transformRequest: angular.identity,
                    headers: { "Content-Type": undefined }
                });
            }
        };
    }

    angular.module("umbraco.resources").factory("redirectResource", ["$http", redirectResource]);
})();
