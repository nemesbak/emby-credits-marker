// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Emby.CreditsMarker
{
    /// <summary>
    /// Wraps <see cref="PluginOptions"/>' property descriptors so their display
    /// name and description are localised for the current UI culture when Emby
    /// builds the settings form (<c>EditorBuilder.BuildFromObject</c> calls
    /// <c>TypeDescriptor.GetProperties</c>). Registered from <see cref="Plugin"/>'s
    /// constructor; falls back to the original English text for any string with
    /// no translation. See <see cref="Localization"/> for why this is needed.
    /// </summary>
    internal sealed class LocalizedTypeDescriptionProvider : TypeDescriptionProvider
    {
        public LocalizedTypeDescriptionProvider(TypeDescriptionProvider parent) : base(parent)
        {
        }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            return new Descriptor(base.GetTypeDescriptor(objectType, instance));
        }

        private sealed class Descriptor : CustomTypeDescriptor
        {
            public Descriptor(ICustomTypeDescriptor parent) : base(parent)
            {
            }

            public override PropertyDescriptorCollection GetProperties()
                => Localise(base.GetProperties());

            public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
                => Localise(base.GetProperties(attributes));

            private static PropertyDescriptorCollection Localise(PropertyDescriptorCollection source)
            {
                var wrapped = new PropertyDescriptor[source.Count];
                for (var i = 0; i < source.Count; i++)
                    wrapped[i] = new LocalizedPropertyDescriptor(source[i]);
                return new PropertyDescriptorCollection(wrapped);
            }
        }

        private sealed class LocalizedPropertyDescriptor : PropertyDescriptor
        {
            private readonly PropertyDescriptor _inner;

            public LocalizedPropertyDescriptor(PropertyDescriptor inner) : base(inner)
            {
                _inner = inner;
            }

            // The two we actually translate. Recomputed on every get, so the
            // result follows CultureInfo.CurrentUICulture per request.
            public override string DisplayName => Localization.T(_inner.DisplayName);

            public override string Description => Localization.T(_inner.Description);

            // Emby's EditorFactoryBase reads the DisplayName off the attribute, not
            // off the descriptor, so translate it there too.
            public override AttributeCollection Attributes
            {
                get
                {
                    var list = new List<Attribute>();
                    foreach (Attribute a in _inner.Attributes)
                    {
                        if (a is DisplayNameAttribute dn)
                            list.Add(new DisplayNameAttribute(Localization.T(dn.DisplayName)));
                        else if (a is DescriptionAttribute de)
                            list.Add(new DescriptionAttribute(Localization.T(de.Description)));
                        else
                            list.Add(a);
                    }
                    return new AttributeCollection(list.ToArray());
                }
            }

            // Everything else is a straight pass-through.
            public override bool CanResetValue(object component) => _inner.CanResetValue(component);
            public override object GetValue(object component) => _inner.GetValue(component);
            public override void ResetValue(object component) => _inner.ResetValue(component);
            public override void SetValue(object component, object value) => _inner.SetValue(component, value);
            public override bool ShouldSerializeValue(object component) => _inner.ShouldSerializeValue(component);
            public override Type ComponentType => _inner.ComponentType;
            public override bool IsReadOnly => _inner.IsReadOnly;
            public override Type PropertyType => _inner.PropertyType;
        }
    }
}
