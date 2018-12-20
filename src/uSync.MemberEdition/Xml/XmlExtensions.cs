using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Schema;

namespace uSync.MemberEdition.Xml
{
	public static class XmlExtensions
	{
		private static class Xsi
		{
			public static XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

			public static XName schemaLocation = xsi + "schemaLocation";
			public static XName noNamespaceSchemaLocation = xsi + "noNamespaceSchemaLocation";
		}

		private static IEnumerable<XAttribute> NormalizeAttributes(XElement element, bool havePSVI = false)
		{
			return element.Attributes()
				.Where(a => !a.IsNamespaceDeclaration &&
					a.Name != Xsi.schemaLocation &&
					a.Name != Xsi.noNamespaceSchemaLocation)
				.OrderBy(a => a.Name.NamespaceName)
				.ThenBy(a => a.Name.LocalName)
				.Select(
					a =>
					{
						if (havePSVI)
						{
							var dt = a.GetSchemaInfo().SchemaType.TypeCode;
							switch (dt)
							{
								case XmlTypeCode.Boolean:
									return new XAttribute(a.Name, (bool)a);
								case XmlTypeCode.DateTime:
									return new XAttribute(a.Name, (DateTime)a);
								case XmlTypeCode.Decimal:
									return new XAttribute(a.Name, (decimal)a);
								case XmlTypeCode.Double:
									return new XAttribute(a.Name, (double)a);
								case XmlTypeCode.Float:
									return new XAttribute(a.Name, (float)a);
								case XmlTypeCode.HexBinary:
								case XmlTypeCode.Language:
									return new XAttribute(a.Name,
										((string)a).ToLower());
							}
						}
						return a;
					}
				);
		}

		public static XNode Normalize(this XNode node, bool havePSVI = false)
		{
			// trim comments and processing instructions from normalized tree
			if (node is XComment || node is XProcessingInstruction)
				return null;
			XElement e = node as XElement;
			if (e != null)
				return Normalize(e, havePSVI);
			// Only thing left is XCData and XText, so clone them
			return node;
		}

		public static XElement Normalize(this XElement element, bool havePSVI = false)
		{
			if (havePSVI)
			{
				var dt = element.GetSchemaInfo();
				switch (dt.SchemaType.TypeCode)
				{
					case XmlTypeCode.Boolean:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							(bool)element);
					case XmlTypeCode.DateTime:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							(DateTime)element);
					case XmlTypeCode.Decimal:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							(decimal)element);
					case XmlTypeCode.Double:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							(double)element);
					case XmlTypeCode.Float:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							(float)element);
					case XmlTypeCode.HexBinary:
					case XmlTypeCode.Language:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							((string)element).ToLower());
					default:
						return new XElement(element.Name,
							NormalizeAttributes(element, havePSVI),
							element.Nodes().Select(n => Normalize(n, havePSVI))
						);
				}
			}
			else
			{
				return new XElement(element.Name,
					NormalizeAttributes(element, havePSVI),
					element.Nodes().Select(n => Normalize(n, havePSVI))
				);
			}
		}
	}
}
