﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Waher.Content.Markdown.Model.SpanElements
{
	/// <summary>
	/// Automatic Link (URL)
	/// </summary>
	public class AutomaticLinkUrl : MarkdownElement
	{
		private string url;

		/// <summary>
		/// Inline HTML.
		/// </summary>
		/// <param name="Document">Markdown document.</param>
		/// <param name="URL">Automatic URL link.</param>
		public AutomaticLinkUrl(MarkdownDocument Document, string URL)
			: base(Document)
		{
			this.url = URL;
		}

		/// <summary>
		/// URL
		/// </summary>
		public string URL
		{
			get { return this.url; }
		}

		/// <summary>
		/// Generates HTML for the markdown element.
		/// </summary>
		/// <param name="Output">HTML will be output here.</param>
		public override void GenerateHTML(StringBuilder Output)
		{
			Output.Append("<a href=\"");
			Output.Append(MarkdownDocument.HtmlAttributeEncode(this.url));
			Output.Append("\">");
			Output.Append(MarkdownDocument.HtmlValueEncode(this.url));
			Output.Append("</a>");
		}

		/// <summary>
		/// Generates plain text for the markdown element.
		/// </summary>
		/// <param name="Output">Plain text will be output here.</param>
		public override void GeneratePlainText(StringBuilder Output)
		{
			Output.Append(this.url);
		}

		/// <summary>
		/// <see cref="Object.ToString()"/>
		/// </summary>
		public override string ToString()
		{
			return this.url;
		}

		/// <summary>
		/// Generates XAML for the markdown element.
		/// </summary>
		/// <param name="Output">XAML will be output here.</param>
		/// <param name="Settings">XAML settings.</param>
		/// <param name="TextAlignment">Alignment of text in element.</param>
		public override void GenerateXAML(XmlWriter Output, XamlSettings Settings, TextAlignment TextAlignment)
		{
			Output.WriteStartElement("Hyperlink");
			Output.WriteAttributeString("NavigateUri", this.url);
			Output.WriteValue(this.url);
			Output.WriteEndElement();			
		}

		/// <summary>
		/// If the element is an inline span element.
		/// </summary>
		internal override bool InlineSpanElement
		{
			get { return true; }
		}
	}
}