(function () {
    "use strict";

    function DashboardController($scope, $window, redirectResource, notificationsService, overlayService) {
        var vm = this;

        vm.loading = true;
        vm.redirects = [];
        vm.showModal = false;
        vm.modalModel = null;
        vm.importInProgress = false;

        vm.statusCodes = [
            { value: 301, label: "301 - Permanent Redirect" },
            { value: 302, label: "302 - Temporary Redirect" },
            { value: 404, label: "404 - Not Found" },
            { value: 410, label: "410 - Gone" }
        ];

        vm.getStatusCodeLabel = function (code) {
            var found = vm.statusCodes.find(function (sc) {
                return sc.value == code;
            });
            return found ? found.label : code;
        };

        vm.loadRedirects = function () {
            vm.loading = true;
            redirectResource.getAll().then(function (response) {
                vm.redirects = response.data;
                vm.loading = false;
            }, function (error) {
                notificationsService.error("Error", "Failed to load redirects");
                vm.loading = false;
            });
        };

        vm.openAddModal = function () {
            vm.modalModel = {
                title: "Add New Redirect",
                redirect: {
                    oldUrl: "",
                    newUrl: "",
                    description: "",
                    statusCode: "301",
                    isActive: true,
                    isRegex: false
                },
                close: function () {
                    vm.closeModal();
                },
                submit: function (model) {
                    vm.saveRedirect(model);
                }
            };
            vm.showModal = true;
        };

        vm.openEditModal = function (redirect) {
            vm.modalModel = {
                title: "Edit Redirect",
                redirect: {
                    id: redirect.id,
                    oldUrl: redirect.oldUrl,
                    newUrl: redirect.newUrl || "",
                    description: redirect.description || "",
                    statusCode: redirect.statusCode.toString(),
                    isActive: redirect.isActive,
                    isRegex: !!redirect.isRegex
                },
                close: function () {
                    vm.closeModal();
                },
                submit: function (model) {
                    vm.saveRedirect(model);
                }
            };
            vm.showModal = true;
        };

        vm.closeModal = function () {
            vm.showModal = false;
            vm.modalModel = null;
        };

        vm.testRedirect = function (redirect) {
            if (!redirect || !redirect.oldUrl) {
                notificationsService.error("Test", "Old URL is missing.");
                return;
            }

            redirectResource.test(redirect.oldUrl).then(function (response) {
                var result = response.data;
                if (!result.matched) {
                    notificationsService.info("Test", "No redirect matched.");
                    return;
                }

                var code = result.redirect && result.redirect.statusCode;
                var to = result.computedNewUrl || "-";
                notificationsService.success("Test", "Matched " + result.matchType + " (" + code + ") -> " + to);
            }, function () {
                notificationsService.error("Test", "Failed to test redirect.");
            });
        };

        vm.saveRedirect = function (model) {
            var redirect = model.redirect;
            redirect.statusCode = parseInt(redirect.statusCode);

            if (!redirect.oldUrl) {
                notificationsService.error("Validation Error", "Old URL is required");
                return;
            }

            if ((redirect.statusCode === 301 || redirect.statusCode === 302) && !redirect.newUrl) {
                notificationsService.error("Validation Error", "New URL is required for redirect status codes");
                return;
            }

            model.submitButtonState = "busy";

            if (redirect.id) {
                redirectResource.update(redirect.id, redirect).then(function () {
                    notificationsService.success("Success", "Redirect updated successfully");
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to update redirect");
                    model.submitButtonState = "error";
                });
            } else {
                redirectResource.create(redirect).then(function () {
                    notificationsService.success("Success", "Redirect created successfully");
                    vm.closeModal();
                    vm.loadRedirects();
                }, function (error) {
                    notificationsService.error("Error", error.data || "Failed to create redirect");
                    model.submitButtonState = "error";
                });
            }
        };

        vm.deleteRedirect = function (redirect) {
            overlayService.confirm({
                title: "Delete Redirect",
                content: "Are you sure you want to delete the redirect for '" + redirect.oldUrl + "'?",
                submitButtonLabel: "Delete",
                submitButtonStyle: "danger",
                close: function () {
                    overlayService.close();
                },
                submit: function () {
                    redirectResource.delete(redirect.id).then(function () {
                        notificationsService.success("Success", "Redirect deleted successfully");
                        vm.loadRedirects();
                        overlayService.close();
                    }, function (error) {
                        notificationsService.error("Error", "Failed to delete redirect");
                        overlayService.close();
                    });
                }
            });
        };

        vm.loadRedirects();

        vm.exportCsv = function () {
            var url = redirectResource.exportUrl();
            // CSV download
            $window.location.href = url;
        };

        vm.triggerImport = function () {
            if (vm.importInProgress) return;
            var input = document.getElementById("redirectManagerImportFileInput");
            if (input) input.click();
        };

        vm.handleImportFile = function (files) {
            var file = files && files.length ? files[0] : null;
            if (!file) return;

            vm.importInProgress = true;

            redirectResource.importCsv(file).then(function (response) {
                var result = response.data || {};
                notificationsService.success(
                    "Import CSV",
                    "Imported. Created: " + (result.created || 0) + ", Updated: " + (result.updated || 0) + ", Skipped: " + (result.skipped || 0)
                );
                vm.loadRedirects();
            }, function (error) {
                var message = (error && error.data) ? error.data : "Import failed";
                notificationsService.error("Import CSV", message);
            }).finally(function () {
                vm.importInProgress = false;
                // reset input so same file can be selected again
                var input = document.getElementById("redirectManagerImportFileInput");
                if (input) input.value = "";
            });
        };
    }

    angular.module("umbraco").controller("RedirectManager.DashboardController", [
        "$scope",
        "$window",
        "redirectResource",
        "notificationsService",
        "overlayService",
        DashboardController
    ]);
})();
