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
            }
        };
    }

    angular.module("umbraco.resources").factory("redirectResource", ["$http", redirectResource]);
})();
