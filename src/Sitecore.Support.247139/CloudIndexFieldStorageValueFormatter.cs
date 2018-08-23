namespace Sitecore.XA.Foundation.Search.Providers.Azure
{

  using Sitecore.ContentSearch;
  using Sitecore.ContentSearch.Abstractions;
  using Sitecore.ContentSearch.Azure;
  using Sitecore.ContentSearch.Azure.Http;
  using Sitecore.ContentSearch.Azure.Models;
  using Sitecore.ContentSearch.Azure.Schema;
  using Sitecore.ContentSearch.Converters;
  using Sitecore.Diagnostics;
  using Sitecore.Exceptions;
  using Sitecore.XA.Foundation.Search.Providers.Azure.Geospatial;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Globalization;
  using System.Linq;
  using System.Xml;


  public class CloudIndexFieldStorageValueFormatter : IndexFieldStorageValueFormatter, ISearchIndexInitializable
  {
    private ICloudSearchIndex _searchIndex;

    public CloudIndexFieldStorageValueFormatter()
    {
      base.EnumerableConverter = new IndexFieldEnumerableConverter(this);
    }

    public override object FormatValueForIndexStorage(object value, string fieldName)
    {
      Assert.IsNotNullOrEmpty(fieldName, "fieldName");
      object obj = value;
      if (obj == null)
      {
        return null;
      }
      IndexedField fieldByCloudName = this._searchIndex.SchemaBuilder.GetSchema().GetFieldByCloudName(fieldName);
      if (fieldByCloudName == null)
      {
        return value;
      }
      ICloudSearchTypeMapper cloudTypeMapper = this._searchIndex.CloudConfiguration.CloudTypeMapper;
      Type type = (fieldByCloudName.Type.ToLower(CultureInfo.InvariantCulture) == "edm.geographypoint") ? typeof(GeoPoint) : cloudTypeMapper.GetNativeType(fieldByCloudName.Type);
      IndexFieldConverterContext context = new IndexFieldConverterContext(fieldName);
      try
      {
        if (obj is IIndexableId)
        {
          obj = this.FormatValueForIndexStorage(((IIndexableId)obj).Value, fieldName);
        }
        else if (obj is IIndexableUniqueId)
        {
          obj = this.FormatValueForIndexStorage(((IIndexableUniqueId)obj).Value, fieldName);
        }
        else
        {
          obj = this.ConvertToType(obj, type, context);
        }
        if (obj != null && !(obj is string) && !type.IsInstanceOfType(obj) && (!(obj is IEnumerable<string>) || !typeof(IEnumerable<string>).IsAssignableFrom(type)))
        {
          throw new InvalidCastException(string.Format("Converted value has type '{0}', but '{1}' is expected.", obj.GetType(), type));
        }
      }
      catch (Exception innerException)
      {
        throw new NotSupportedException(string.Format("Field '{0}' with value '{1}' of type '{2}' cannot be converted to type '{3}' declared for the field in the schema.", new object[]
        {
                    fieldName,
                    value,
                    value.GetType(),
                    type
        }), innerException);
      }
      return obj;
    }

    public override object ReadFromIndexStorage(object indexValue, string fieldName, Type destinationType)
    {
      if (indexValue == null)
      {
        return null;
      }
      if (destinationType == null)
      {
        throw new ArgumentNullException("destinationType");
      }
      if (indexValue.GetType() == destinationType)
      {
        return indexValue;
      }
      if (indexValue is IEnumerable && !(indexValue is string))
      {
        object[] array = (indexValue as IEnumerable).Cast<object>().ToArray<object>();
        if (array.Length == 0)
        {
          return null;
        }
        if (array.Length == 1)
        {
          return this.ReadFromIndexStorageBase(array[0], fieldName, destinationType);
        }
      }
      object obj = this.ReadFromIndexStorageBase(indexValue, fieldName, destinationType);
      if (obj != null || !(destinationType != typeof(string)) || !typeof(IEnumerable).IsAssignableFrom(destinationType))
      {
        return obj;
      }
      if (destinationType.IsInterface)
      {
        return Activator.CreateInstance(typeof(List<>).MakeGenericType(destinationType.GetGenericArguments()));
      }
      return Activator.CreateInstance(destinationType);
    }

    public override void AddConverter(XmlNode configNode)
    {
      XmlAttribute expr_10 = configNode.Attributes["handlesType"];
      if (expr_10 == null)
      {
        throw new ConfigurationException("Attribute 'handlesType' is required.");
      }
      if (configNode.Attributes["typeConverter"] == null)
      {
        throw new ConfigurationException("Attribute 'typeConverter' is required.");
      }
      string arg_8E_0 = expr_10.Value;
      string value = configNode.Attributes["typeConverter"].Value;
      XmlDocument xmlDocument = new XmlDocument();
      if (configNode.HasChildNodes)
      {
        xmlDocument.LoadXml(string.Format("<converter type=\"{0}\">{1}</converter>", value, configNode.InnerXml));
      }
      else
      {
        xmlDocument.LoadXml(string.Format("<converter type=\"{0}\" />", value));
      }
      Type type = Type.GetType(arg_8E_0);
      ICloudSearchIndex expr_9A = this._searchIndex;
      IFactoryWrapper arg_B2_0;
      if (expr_9A == null)
      {
        arg_B2_0 = null;
      }
      else
      {
        IObjectLocator expr_A6 = expr_9A.Locator;
        arg_B2_0 = ((expr_A6 != null) ? expr_A6.GetInstance<IFactoryWrapper>() : null);
      }
      TypeConverter converter = (arg_B2_0 ?? new FactoryWrapper()).CreateObject<TypeConverter>(xmlDocument.DocumentElement, true);
      base.AddConverter(type, converter);
    }

    public new void Initialize(ISearchIndex searchIndex)
    {
      ICloudSearchIndex cloudSearchIndex = searchIndex as ICloudSearchIndex;
      ICloudSearchIndex expr_09 = cloudSearchIndex;
      if (expr_09 == null)
      {
        throw new NotSupportedException(string.Format("Only {0} is supported", typeof(CloudSearchProviderIndex).Name));
      }
      this._searchIndex = expr_09;
      base.Initialize(searchIndex);
    }

    private TypeConverter GetConverter(Type type)
    {
      TypeConverter typeConverter = base.Converters.GetTypeConverter(type);
      if (typeConverter == null)
      {
        Type[] interfaces = type.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
          Type type2 = interfaces[i];
          typeConverter = base.Converters.GetTypeConverter(type2);
          if (typeConverter != null)
          {
            return typeConverter;
          }
        }
      }
      return typeConverter;
    }

    private object ConvertToType(object value, Type expectedType, ITypeDescriptorContext context)
    {
      Type type = value.GetType();
      if (type == expectedType)
      {
        return value;
      }
      if (typeof(IEnumerable<string>).IsAssignableFrom(type) && typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        return value;
      }
      TypeConverter converter = this.GetConverter(value.GetType());
      if (converter != null && converter.CanConvertTo(context, expectedType))
      {
        return converter.ConvertTo(context, CultureInfo.CurrentCulture, value, expectedType);
      }
      object result;
      if (this.TryConvertToPrimitiveType(value, expectedType, context, out result))
      {
        return result;
      }
      if (this.TryConvertToEnumerable(value, expectedType, context, out result))
      {
        return result;
      }
      throw new InvalidCastException(string.Format("Cannon cast value '{0}' of type '{1}' to '{2}'.", value, value.GetType(), expectedType));
    }

    private bool TryConvertToPrimitiveType(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (value as string == string.Empty && expectedType.IsValueType)
      {
        result = Activator.CreateInstance(expectedType);
        return true;
      }
      if (value is string)
      {
        string text = (string)value;
        if (expectedType == typeof(bool))
        {
          if (text == "1")
          {
            result = true;
            return true;
          }
          if (text == "0" || text == string.Empty)
          {
            result = false;
            return true;
          }
        }
        else if (expectedType == typeof(DateTimeOffset))
        {
          if (text.Length > 15 && text[15] == ':')
          {
            result = DateUtil.ParseDateTime(text, DateTime.MinValue);
          }
          else
          {
            result = DateTimeOffset.Parse(text);
          }
          return true;
        }
      }
      if (value is IConvertible && (expectedType == typeof(bool) || expectedType == typeof(string) || expectedType == typeof(int) || expectedType == typeof(long) || expectedType == typeof(double) || expectedType == typeof(float)))
      {
        result = System.Convert.ChangeType(value, expectedType);
        return true;
      }
      result = null;
      return false;
    }

    private bool TryConvertToEnumerable(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        if (value is string || !(value is IEnumerable))
        {
          object obj = this.ConvertToType(value, typeof(string), context);
          if (!(obj is string))
          {
            result = null;
            return false;
          }
          result = new string[]
          {
                        (string)obj
          };
          return true;
        }
        else if (!(value is IEnumerable<string>))
        {
          List<string> list = new List<string>();
          foreach (object current in ((IEnumerable)value))
          {
            object obj2 = this.ConvertToType(current, typeof(string), context);
            if (!(obj2 is string))
            {
              result = null;
              return false;
            }
            list.Add((string)obj2);
          }
          result = list.ToArray();
          return true;
        }
      }
      result = null;
      return false;
    }

    private object ReadFromIndexStorageBase(object indexValue, string fieldName, Type destinationType)
    {
      if (indexValue == null)
      {
        return null;
      }
      if (destinationType == null)
      {
        throw new ArgumentNullException("destinationType");
      }
      if (destinationType.IsInstanceOfType(indexValue))
      {
        return indexValue;
      }
      object result;
      try
      {
        IndexFieldConverterContext context = new IndexFieldConverterContext(fieldName);
        TypeConverter typeConverter = base.Converters.GetTypeConverter(destinationType);
        if (base.EnumerableConverter != null && base.EnumerableConverter.CanConvertTo(destinationType))
        {
          if (indexValue is IEnumerable && indexValue.GetType() != typeof(string))
          {
            result = base.EnumerableConverter.ConvertTo(context, CultureInfo.InvariantCulture, indexValue, destinationType);
            return result;
          }
          if (destinationType != typeof(string) && !indexValue.Equals(string.Empty))
          {
            result = base.EnumerableConverter.ConvertTo(context, CultureInfo.InvariantCulture, new object[]
            {
                            indexValue
            }, destinationType);
            return result;
          }
        }
        if (typeof(IConvertible).IsAssignableFrom(destinationType) && !indexValue.Equals(string.Empty))
        {
          result = System.Convert.ChangeType(indexValue, destinationType);
        }
        else
        {
          result = ((typeConverter != null) ? typeConverter.ConvertFrom(context, CultureInfo.InvariantCulture, indexValue) : null);
        }
      }
      catch (InvalidCastException ex)
      {
        throw new InvalidCastException(string.Format("Could not convert value of type {0} to destination type {1}: {2}", indexValue.GetType().FullName, destinationType.FullName, ex.Message), ex);
      }
      return result;
    }
  }
}