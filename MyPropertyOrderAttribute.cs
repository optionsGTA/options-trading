using System;
using System.Runtime.CompilerServices;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot {
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class MyPropertyOrderAttribute : Attribute {
        public int Order {get;}

        public MyPropertyOrderAttribute([CallerLineNumber] int order = 0) {
            Order = order;
        }
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    class AutoPropertyOrderAttribute : PropertyOrderAttribute {
        public AutoPropertyOrderAttribute([CallerLineNumber] int order = 0) : base(order) {}
        public AutoPropertyOrderAttribute(int order, UsageContextEnum usageContext) : base(order, usageContext) {}
    }
}
