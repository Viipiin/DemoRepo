using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRAutomation.Plugins
{
    public sealed class LocalContext
    {
        public LocalContext(IServiceProvider serviceProvider)
        {
            Context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            Trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            UserService = factory.CreateOrganizationService(Context.UserId);
            // Runs as SYSTEM. Used only for housekeeping the calling user may not have rights to do
            // (setting owners, syncing the user hierarchy, writing history).
            SystemService = factory.CreateOrganizationService(null);
        }

        public IPluginExecutionContext Context { get; }
        public ITracingService Trace { get; }
        public IOrganizationService UserService { get; }
        public IOrganizationService SystemService { get; }

        public bool IsCreate => Context.MessageName == "Create";

        public Entity Target => Context.InputParameters.Contains("Target") ? Context.InputParameters["Target"] as Entity : null;

        public Entity PreImage => Context.PreEntityImages.Contains("PreImage") ? Context.PreEntityImages["PreImage"] : null;

        /// <summary>Value from the target if it is being set, otherwise from the pre-image.</summary>
        public T Merged<T>(string attribute)
        {
            var target = Target;
            if (target != null && target.Contains(attribute)) return target.GetAttributeValue<T>(attribute);
            var pre = PreImage;
            return pre != null ? pre.GetAttributeValue<T>(attribute) : default(T);
        }

        /// <summary>True when the attribute is in the target and its value differs from the pre-image.</summary>
        public bool Changed(string attribute)
        {
            var target = Target;
            if (target == null || !target.Contains(attribute)) return false;
            if (IsCreate) return target[attribute] != null;
            var before = PreImage != null && PreImage.Contains(attribute) ? PreImage[attribute] : null;
            return !SameValue(before, target[attribute]);
        }

        private static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is EntityReference ra && b is EntityReference rb) return ra.Id == rb.Id;
            if (a is OptionSetValue oa && b is OptionSetValue ob) return oa.Value == ob.Value;
            if (a is Money ma && b is Money mb) return ma.Value == mb.Value;
            return a.Equals(b);
        }

        /// <summary>Reads an environment variable's current value, falling back to its default value.</summary>
        public string GetEnvironmentVariable(string schemaName)
        {
            var query = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("defaultvalue"),
                TopCount = 1
            };
            query.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            var valueLink = query.AddLink("environmentvariablevalue", "environmentvariabledefinitionid", "environmentvariabledefinitionid", JoinOperator.LeftOuter);
            valueLink.EntityAlias = "v";
            valueLink.Columns = new ColumnSet("value");

            var result = SystemService.RetrieveMultiple(query).Entities;
            if (result.Count == 0) return null;
            var current = result[0].GetAttributeValue<AliasedValue>("v.value");
            return current?.Value as string ?? result[0].GetAttributeValue<string>("defaultvalue");
        }
    }

    public abstract class PluginBase : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var local = new LocalContext(serviceProvider);
            local.Trace.Trace("{0} on {1} ({2}), depth {3}", GetType().Name, local.Context.PrimaryEntityName, local.Context.MessageName, local.Context.Depth);
            try
            {
                ExecuteInternal(local);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                local.Trace.Trace(ex.ToString());
                throw new InvalidPluginExecutionException("HR Automation: " + ex.Detail.Message, ex);
            }
            catch (Exception ex)
            {
                local.Trace.Trace(ex.ToString());
                throw new InvalidPluginExecutionException("HR Automation: " + ex.Message, ex);
            }
        }

        protected abstract void ExecuteInternal(LocalContext local);
    }
}
