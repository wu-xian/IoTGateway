﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Waher.Content.Emoji
{
	/// <summary>
	/// Interface for Emoji sources. Emoji sources provide emojis to content providers.
	/// </summary>
	public interface IEmojiSource
	{
		/// <summary>
		/// If the emoji is supported by the emoji source.
		/// </summary>
		/// <param name="Emoji">Emoji</param>
		/// <returns>If emoji is supported.</returns>
		bool EmojiSupported(EmojiInfo Emoji);

		/// <summary>
		/// Generates HTML for a given Emoji.
		/// </summary>
		/// <param name="Output">Output</param>
		/// <param name="Emoji">Emoji</param>
		void GenerateHTML(StringBuilder Output, EmojiInfo Emoji);

		/// <summary>
		/// Gets the image source of an emoji.
		/// </summary>
		/// <param name="Emoji">Emoji</param>
		/// <param name="Url">URL to emoji.</param>
		/// <param name="Width">Width of emoji.</param>
		/// <param name="Height">Height of emoji.</param>
		void GetImageSource(EmojiInfo Emoji, out string Url, out int Width, out int Height);
	}
}