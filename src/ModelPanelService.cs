using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexRouterSwitch
{
    // ------------------------------------------------------------------
    // 模型管理服务：调用 tools/model-panel.mjs 桥，读写 codex-router 的模型
    // 可见性状态。所有写操作由桥经官方跨进程锁与发布管线完成。
    // ------------------------------------------------------------------

    internal sealed class ModelEntry
    {
        public string Slug;
        public string Name;
        public bool Visible;
        public bool Toggleable;
        public string Note;
    }

    internal sealed class CompanyGroup
    {
        public string Id;
        public string Name;
        public int Total;
        public int Visible;
        public readonly List<ModelEntry> Models = new List<ModelEntry>();
    }

    internal sealed class ProviderGroup
    {
        public string Id;
        public string Name;
        public int Total;
        public int Visible;
        public readonly List<CompanyGroup> Companies = new List<CompanyGroup>();
    }

    internal sealed class PanelSnapshot
    {
        public string CodexMode;
        public int Total;
        public int VisibleCount;
        public readonly List<ProviderGroup> Providers = new List<ProviderGroup>();
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class PanelApplyResult
    {
        public int Shown;
        public int Hidden;
        public string BackupDir;
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class DiscoveredModel
    {
        public string Id;
        public string Name;
        public long ContextWindow;
    }

    internal sealed class DiscoveredProvider
    {
        public string Id;
        public string Name;
        public string Error;
        public int AddableTotal;
        public int BlockedTotal;
        public readonly List<DiscoveredModel> Addable = new List<DiscoveredModel>();
    }

    internal sealed class PanelDiscoverResult
    {
        public readonly List<DiscoveredProvider> Providers = new List<DiscoveredProvider>();
    }

    internal sealed class PanelAddResult
    {
        public int Added;
        public readonly List<string> SavedSlugs = new List<string>();
        public string Note;
        public readonly List<string> Rejections = new List<string>();
    }

    internal sealed class ModelPanelService
    {
        private readonly RouterController controller;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public ModelPanelService(RouterController controller)
        {
            this.controller = controller;
        }

        public PanelSnapshot Load()
        {
            Dictionary<string, object> root = ParseObject(
                controller.RunModelPanelCommand(new string[] { "list" }, 180000)
            );
            RequireOk(root, "模型列表读取失败。");

            PanelSnapshot snapshot = new PanelSnapshot();
            snapshot.CodexMode = ValueAsString(Get(root, "codexMode"));
            Dictionary<string, object> stats = AsDictionary(Get(root, "stats"));
            snapshot.Total = ValueAsInt(stats, "total");
            snapshot.VisibleCount = ValueAsInt(stats, "visible");

            foreach (object providerRaw in AsArray(Get(root, "providers")))
            {
                Dictionary<string, object> providerDict = providerRaw as Dictionary<string, object>;
                if (providerDict == null)
                {
                    continue;
                }
                ProviderGroup provider = new ProviderGroup();
                provider.Id = ValueAsString(Get(providerDict, "id"));
                provider.Name = ValueAsString(Get(providerDict, "name"));
                provider.Total = ValueAsInt(providerDict, "total");
                provider.Visible = ValueAsInt(providerDict, "visible");

                foreach (object companyRaw in AsArray(Get(providerDict, "companies")))
                {
                    Dictionary<string, object> companyDict = companyRaw as Dictionary<string, object>;
                    if (companyDict == null)
                    {
                        continue;
                    }
                    CompanyGroup company = new CompanyGroup();
                    company.Id = ValueAsString(Get(companyDict, "id"));
                    company.Name = ValueAsString(Get(companyDict, "name"));
                    company.Total = ValueAsInt(companyDict, "total");
                    company.Visible = ValueAsInt(companyDict, "visible");

                    foreach (object modelRaw in AsArray(Get(companyDict, "models")))
                    {
                        Dictionary<string, object> modelDict = modelRaw as Dictionary<string, object>;
                        if (modelDict == null)
                        {
                            continue;
                        }
                        ModelEntry model = new ModelEntry();
                        model.Slug = ValueAsString(Get(modelDict, "slug"));
                        model.Name = ValueAsString(Get(modelDict, "name"));
                        model.Visible = ValueAsBool(Get(modelDict, "visible"));
                        model.Toggleable = ValueAsBool(Get(modelDict, "toggleable"));
                        model.Note = ValueAsString(Get(modelDict, "note"));
                        company.Models.Add(model);
                    }
                    provider.Companies.Add(company);
                }
                snapshot.Providers.Add(provider);
            }

            foreach (object warningRaw in AsArray(Get(root, "warnings")))
            {
                string warning = ValueAsString(warningRaw);
                if (!String.IsNullOrWhiteSpace(warning))
                {
                    snapshot.Warnings.Add(warning);
                }
            }
            return snapshot;
        }

        public PanelApplyResult Apply(List<string> show, List<string> hide)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["show"] = show ?? new List<string>();
            payload["hide"] = hide ?? new List<string>();

            Dictionary<string, object> root = ParseObject(
                controller.RunModelPanelWithPayload(
                    "apply",
                    json.Serialize(payload),
                    420000
                )
            );
            RequireOk(root, "应用更改失败。");

            PanelApplyResult result = new PanelApplyResult();
            result.Shown = ValueAsInt(root, "shown");
            result.Hidden = ValueAsInt(root, "hidden");
            result.BackupDir = ValueAsString(Get(root, "backupDir"));
            foreach (object warningRaw in AsArray(Get(root, "warnings")))
            {
                string warning = ValueAsString(warningRaw);
                if (!String.IsNullOrWhiteSpace(warning))
                {
                    result.Warnings.Add(warning);
                }
            }
            return result;
        }

        public PanelDiscoverResult Discover()
        {
            Dictionary<string, object> root = ParseObject(
                controller.RunModelPanelCommand(new string[] { "discover" }, 300000)
            );
            RequireOk(root, "联网检查失败。");

            PanelDiscoverResult result = new PanelDiscoverResult();
            foreach (object providerRaw in AsArray(Get(root, "providers")))
            {
                Dictionary<string, object> providerDict = providerRaw as Dictionary<string, object>;
                if (providerDict == null)
                {
                    continue;
                }
                DiscoveredProvider provider = new DiscoveredProvider();
                provider.Id = ValueAsString(Get(providerDict, "id"));
                provider.Name = ValueAsString(Get(providerDict, "name"));
                provider.Error = ValueAsString(Get(providerDict, "error"));
                provider.AddableTotal = ValueAsInt(providerDict, "addableTotal");
                provider.BlockedTotal = ValueAsInt(providerDict, "blockedTotal");

                foreach (object modelRaw in AsArray(Get(providerDict, "addable")))
                {
                    Dictionary<string, object> modelDict = modelRaw as Dictionary<string, object>;
                    if (modelDict == null)
                    {
                        continue;
                    }
                    DiscoveredModel model = new DiscoveredModel();
                    model.Id = ValueAsString(Get(modelDict, "id"));
                    model.Name = ValueAsString(Get(modelDict, "name"));
                    model.ContextWindow = ValueAsLong(Get(modelDict, "contextWindow"));
                    if (!String.IsNullOrWhiteSpace(model.Id))
                    {
                        provider.Addable.Add(model);
                    }
                }
                result.Providers.Add(provider);
            }
            return result;
        }

        public PanelAddResult Add(string providerId, List<DiscoveredModel> models)
        {
            List<object> items = new List<object>();
            foreach (DiscoveredModel model in models)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = model.Id;
                if (!String.IsNullOrWhiteSpace(model.Name))
                {
                    item["name"] = model.Name;
                }
                if (model.ContextWindow > 0 && model.ContextWindow <= Int32.MaxValue)
                {
                    item["contextWindow"] = (int)model.ContextWindow;
                }
                items.Add(item);
            }

            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["provider"] = providerId;
            payload["models"] = items;

            Dictionary<string, object> root = ParseObject(
                controller.RunModelPanelWithPayload(
                    "add",
                    json.Serialize(payload),
                    420000
                )
            );
            RequireOk(root, "加入模型失败。");

            PanelAddResult result = new PanelAddResult();
            result.Added = ValueAsInt(root, "added");
            result.Note = ValueAsString(Get(root, "note"));
            foreach (object slugRaw in AsArray(Get(root, "savedSlugs")))
            {
                string slug = ValueAsString(slugRaw);
                if (!String.IsNullOrWhiteSpace(slug))
                {
                    result.SavedSlugs.Add(slug);
                }
            }
            foreach (object skippedRaw in AsArray(Get(root, "skipped")))
            {
                Dictionary<string, object> skippedDict = skippedRaw as Dictionary<string, object>;
                if (skippedDict == null)
                {
                    continue;
                }
                string id = ValueAsString(Get(skippedDict, "id"));
                string reason = ValueAsString(Get(skippedDict, "reason"));
                if (!String.IsNullOrWhiteSpace(id))
                {
                    result.Rejections.Add(
                        id + "：" + (String.IsNullOrWhiteSpace(reason) ? "无法加入。" : reason)
                    );
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // JSON 解析辅助
        // ------------------------------------------------------------------

        private Dictionary<string, object> ParseObject(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("模型管理组件没有返回数据。");
            }
            try
            {
                Dictionary<string, object> parsed =
                    json.DeserializeObject(raw) as Dictionary<string, object>;
                if (parsed == null)
                {
                    throw new InvalidOperationException("模型管理组件返回了无法识别的数据。");
                }
                return parsed;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "模型管理组件返回了无法解析的数据：" + error.Message
                );
            }
        }

        private static void RequireOk(Dictionary<string, object> root, string fallback)
        {
            if (ValueAsBool(Get(root, "ok")))
            {
                return;
            }
            string message = ValueAsString(Get(root, "error"));
            throw new InvalidOperationException(
                String.IsNullOrWhiteSpace(message) ? fallback : message
            );
        }

        private static object Get(Dictionary<string, object> dict, string key)
        {
            if (dict == null)
            {
                return null;
            }
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private static object[] AsArray(object value)
        {
            object[] array = value as object[];
            return array ?? new object[0];
        }

        private static Dictionary<string, object> AsDictionary(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static string ValueAsString(object value)
        {
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int ValueAsInt(Dictionary<string, object> dict, string key)
        {
            object value = Get(dict, key);
            if (value == null)
            {
                return 0;
            }
            int parsed;
            return Int32.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed
            ) ? parsed : 0;
        }

        private static long ValueAsLong(object value)
        {
            if (value == null)
            {
                return 0;
            }
            long parsed;
            return Int64.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed
            ) ? parsed : 0;
        }

        private static bool ValueAsBool(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }
            if (value == null)
            {
                return false;
            }
            bool parsed;
            return Boolean.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                out parsed
            ) && parsed;
        }
    }
}
