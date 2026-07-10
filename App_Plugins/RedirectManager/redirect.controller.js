(function () {
    "use strict";

    function DashboardController($scope, $window, redirectResource, notificationsService, overlayService) {
        var vm = this;

        vm.loading = true;
        vm.redirects = [];
        vm.showModal = false;
        vm.modalModel = null;
        vm.importInProgress = false;
        vm.activeTab = 'redirects';
        vm.missedRequests = [];
        vm.missedLoading = false;
        vm.stats = null;
        vm.statsLoading = false;
        vm.telemetryEnabled = false;
        vm.telemetryLoading = false;
        vm.telemetryDecided = true;
        vm.showTelemetryPrompt = false;
        vm.updateAvailable = false;
        vm.currentVersion = '';
        vm.latestVersion = '';

        vm.setActiveTab = function (tab) {
            vm.activeTab = tab;
        };

        vm.loadStats = function () {
            vm.statsLoading = true;
            redirectResource.getStats().then(function (response) {
                vm.stats = response.data;
                vm.statsLoading = false;
            }, function () {
                notificationsService.error("Error", "Failed to load overview");
                vm.statsLoading = false;
            });
        };

        vm.loadMissedRequests = function () {
            vm.missedLoading = true;
            redirectResource.getMissed().then(function (response) {
                vm.missedRequests = response.data;
                vm.missedLoading = false;
            }, function () {
                notificationsService.error("Error", "Failed to load 404 log");
                vm.missedLoading = false;
            });
        };

        vm.dismissMissedRequest = function (item) {
            redirectResource.dismissMissed(item.id).then(function () {
                vm.missedRequests = vm.missedRequests.filter(function (m) { return m.id !== item.id; });
            }, function () {
                notificationsService.error("Error", "Failed to dismiss entry");
            });
        };

        vm.createRedirectFromMissed = function (item) {
            vm.openAddModal(item.path);
        };

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

        vm.getScheduleBadge = function (redirect) {
            var now = new Date();
            if (redirect.validFrom && new Date(redirect.validFrom) > now) {
                return "Scheduled";
            }
            if (redirect.validUntil && new Date(redirect.validUntil) < now) {
                return "Expired";
            }
            return null;
        };

        vm.getMatchTypeLabel = function (redirect) {
            if (redirect.isRegex) {
                return "Regex";
            }
            if (redirect.oldUrl && redirect.oldUrl.indexOf("*") !== -1) {
                return "Wildcard";
            }
            return "Exact";
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

        vm.openAddModal = function (prefillOldUrl) {
            vm.modalModel = {
                title: "Add New Redirect",
                redirect: {
                    oldUrl: prefillOldUrl || "",
                    newUrl: "",
                    domain: "",
                    description: "",
                    statusCode: "301",
                    isActive: true,
                    isRegex: false,
                    abTestEnabled: false,
                    variantBUrl: "",
                    variantBWeight: 50,
                    preserveQueryString: false,
                    validFrom: null,
                    validUntil: null
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
                    domain: redirect.domain || "",
                    description: redirect.description || "",
                    statusCode: redirect.statusCode.toString(),
                    isActive: redirect.isActive,
                    isRegex: !!redirect.isRegex,
                    abTestEnabled: !!redirect.variantBUrl,
                    variantBUrl: redirect.variantBUrl || "",
                    variantBWeight: redirect.variantBWeight != null ? redirect.variantBWeight : 50,
                    preserveQueryString: !!redirect.preserveQueryString,
                    validFrom: redirect.validFrom ? new Date(redirect.validFrom) : null,
                    validUntil: redirect.validUntil ? new Date(redirect.validUntil) : null
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

            if ((redirect.oldUrl.match(/\*/g) || []).length > 1) {
                notificationsService.error("Validation Error", "Old URL can only contain one wildcard (*)");
                return;
            }

            if ((redirect.statusCode === 301 || redirect.statusCode === 302) && !redirect.newUrl) {
                notificationsService.error("Validation Error", "New URL is required for redirect status codes");
                return;
            }

            if ((redirect.newUrl.match(/\*/g) || []).length > 1) {
                notificationsService.error("Validation Error", "New URL can only contain one wildcard (*)");
                return;
            }

            if (redirect.abTestEnabled && !redirect.variantBUrl) {
                notificationsService.error("Validation Error", "Variant B URL is required when A/B test is enabled");
                return;
            }

            if (redirect.validFrom && redirect.validUntil && new Date(redirect.validUntil) < new Date(redirect.validFrom)) {
                notificationsService.error("Validation Error", "Valid until must be after Valid from");
                return;
            }

            if (redirect.abTestEnabled) {
                redirect.variantBWeight = parseInt(redirect.variantBWeight, 10);
            } else {
                redirect.variantBUrl = null;
                redirect.variantBWeight = null;
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

        vm.loadTelemetryStatus = function () {
            redirectResource.getTelemetryStatus().then(function (response) {
                vm.telemetryEnabled = !!response.data.enabled;
                vm.telemetryDecided = !!response.data.decided;
                vm.showTelemetryPrompt = !vm.telemetryDecided;
            });
        };

        // Always-on update-availability check (no opt-in — no site data is
        // sent, only a public NuGet.org listing is read).
        vm.loadUpdateStatus = function () {
            redirectResource.getUpdateStatus().then(function (response) {
                vm.updateAvailable = !!response.data.updateAvailable;
                vm.currentVersion = response.data.currentVersion || '';
                vm.latestVersion = response.data.latestVersion || '';
            });
        };

        vm.setTelemetryEnabled = function (enabled) {
            vm.telemetryLoading = true;
            var request = enabled ? redirectResource.enableTelemetry() : redirectResource.disableTelemetry();
            request.then(function () {
                vm.telemetryEnabled = enabled;
                vm.telemetryDecided = true;
                vm.showTelemetryPrompt = false;
                vm.telemetryLoading = false;
            }, function () {
                vm.telemetryLoading = false;
                notificationsService.error("Error", "Failed to update telemetry setting");
            });
        };

        vm.toggleTelemetryEnabled = function () {
            vm.setTelemetryEnabled(vm.telemetryEnabled);
        };

        vm.acceptTelemetryPrompt = function () {
            vm.setTelemetryEnabled(true);
        };

        vm.declineTelemetryPrompt = function () {
            vm.setTelemetryEnabled(false);
        };

        vm.loadRedirects();
        vm.loadMissedRequests();
        vm.loadStats();
        vm.loadTelemetryStatus();
        vm.loadUpdateStatus();

        // Opt-in usage ping (no-op if telemetry is disabled/unconfigured server-side); never blocks dashboard load.
        redirectResource.pingTelemetry().catch(function () { });

        vm.exportCsv = function () {
            var url = redirectResource.exportUrl();
            // CSV download
            $window.location.href = url;
        };

        vm.exportStats = function () {
            $window.location.href = redirectResource.statsExportUrl();
        };

        vm.triggerImport = function () {
            if (vm.importInProgress) return;
            var input = document.getElementById("redirectManagerImportFileInput");
            if (input) input.click();
        };

        vm.handleImportFile = function (files) {
            var file = files && files.length ? files[0] : null;
            if (!file) return;

            var reader = new FileReader();

            reader.onload = function (e) {
                var content = e.target.result || "";

                $scope.$apply(function () {
                    vm.importInProgress = true;

                    redirectResource.importCsvContent(content).then(function (response) {
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
                });
            };

            reader.readAsText(file);
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
