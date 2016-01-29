﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Threading;
using Waher.Content;
using Waher.Events;
using Waher.Networking;
using Waher.Networking.Sniffers;
using Waher.Networking.XMPP.Authentication;
using Waher.Networking.XMPP.AuthenticationErrors;
using Waher.Networking.XMPP.StanzaErrors;
using Waher.Networking.XMPP.StreamErrors;
using Waher.Networking.XMPP.DataForms;
using Waher.Networking.XMPP.ServiceDiscovery;
using Waher.Networking.XMPP.SoftwareVersion;
using Waher.Networking.XMPP.Search;

namespace Waher.Networking.XMPP
{
	/// <summary>
	/// Connection error event handler delegate.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="Exception">Information about error received.</param>
	public delegate void XmppExceptionEventHandler(object Sender, Exception Exception);

	/// <summary>
	/// Event handler delegate for state change events.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="NewState">New state reported.</param>
	public delegate void StateChangedEventHandler(object Sender, XmppState NewState);

	/// <summary>
	/// Delegate for IQ result callback methods.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="e">Event arguments.</param>
	public delegate void IqResultEventHandler(object Sender, IqResultEventArgs e);

	/// <summary>
	/// Delegate for IQ get and set handlers.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="e">Event arguments.</param>
	public delegate void IqEventHandler(object Sender, IqEventArgs e);

	/// <summary>
	/// Delegate for Presence events.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="e">Event arguments.</param>
	public delegate void PresenceEventHandler(object Sender, PresenceEventArgs e);

	/// <summary>
	/// Delegate for Message events.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="e">Event arguments.</param>
	public delegate void MessageEventHandler(object Sender, MessageEventArgs e);

	/// <summary>
	/// Delegate for Roster Item events.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="Item">Roster Item</param>
	public delegate void RosterItemEventHandler(object Sender, RosterItem Item);

	/// <summary>
	/// Delegate for Dynamic Data Form events.
	/// </summary>
	/// <param name="Sender">Sender of event.</param>
	/// <param name="e">Event arguments.</param>
	public delegate void DynamicDataFormEventHandler(object Sender, DynamicDataFormEventArgs e);

	/// <summary>
	/// Manages an XMPP client connection. Implements XMPP, as defined in
	/// https://tools.ietf.org/html/rfc6120
	/// https://tools.ietf.org/html/rfc6121
	/// https://tools.ietf.org/html/rfc6122
	/// 
	/// Extensions supported directly by client object:
	/// 
	/// XEP-0030: Service Discovery: http://xmpp.org/extensions/xep-0030.html
	/// XEP-0055: Jabber Search: http://xmpp.org/extensions/xep-0055.html
	/// XEP-0077: In-band Registration: http://xmpp.org/extensions/xep-0077.html
	/// XEP-0092: Software Version: http://xmpp.org/extensions/xep-0092.html
	/// 
	/// Quality of Service: http://xmpp.org/extensions/inbox/qos.html
	/// </summary>
	public class XmppClient : Sniffable, IDisposable
	{
		/// <summary>
		/// urn:ietf:params:xml:ns:xmpp-streams
		/// </summary>
		public const string NamespaceXmppStreams = "urn:ietf:params:xml:ns:xmpp-streams";

		/// <summary>
		/// urn:ietf:params:xml:ns:xmpp-stanzas
		/// </summary>
		public const string NamespaceXmppStanzas = "urn:ietf:params:xml:ns:xmpp-stanzas";

		/// <summary>
		/// urn:ietf:params:xml:ns:xmpp-sasl
		/// </summary>
		public const string NamespaceXmppSasl = "urn:ietf:params:xml:ns:xmpp-sasl";

		/// <summary>
		/// jabber:iq:register
		/// </summary>
		public const string NamespaceRegister = "jabber:iq:register";

		/// <summary>
		/// jabber:x:data
		/// </summary>
		public const string NamespaceData = "jabber:x:data";

		/// <summary>
		/// http://jabber.org/protocol/xdata-validate
		/// </summary>
		public const string NamespaceDataValidate = "http://jabber.org/protocol/xdata-validate";

		/// <summary>
		/// http://jabber.org/protocol/xdata-layout
		/// </summary>
		public const string NamespaceDataLayout = "http://jabber.org/protocol/xdata-layout";

		/// <summary>
		/// jabber:iq:roster
		/// </summary>
		public const string NamespaceRoster = "jabber:iq:roster";

		/// <summary>
		/// urn:xmpp:xdata:dynamic
		/// </summary>
		public const string NamespaceDynamicForms = "urn:xmpp:xdata:dynamic";

		/// <summary>
		/// http://jabber.org/protocol/disco#info
		/// </summary>
		public const string NamespaceServiceDiscoveryInfo = "http://jabber.org/protocol/disco#info";

		/// <summary>
		/// http://jabber.org/protocol/disco#items
		/// </summary>
		public const string NamespaceServiceDiscoveryItems = "http://jabber.org/protocol/disco#items";

		/// <summary>
		/// jabber:iq:version
		/// </summary>
		public const string NamespaceSoftwareVersion = "jabber:iq:version";

		/// <summary>
		/// jabber:iq:search
		/// </summary>
		public const string NamespaceSearch = "jabber:iq:search";

		/// <summary>
		/// urn:xmpp:qos
		/// </summary>
		public const string NamespaceQualityOfService = "urn:xmpp:qos";

		/// <summary>
		/// Regular expression for Full JIDs
		/// </summary>
		public static readonly Regex FullJidRegEx = new Regex("^(?:([^@/<>'\\\"\\s]+)@)([^@/<>'\\\"\\s]+)(?:/([^<>'\\\"\\s]*))?$", RegexOptions.Singleline | RegexOptions.Compiled);

		/// <summary>
		/// Regular expression for Bare JIDs
		/// </summary>
		public static readonly Regex BareJidRegEx = new Regex("^(?:([^@/<>'\\\"\\s]+)@)([^@/<>'\\\"\\s]+)$", RegexOptions.Singleline | RegexOptions.Compiled);


		private const int BufferSize = 16384;
		private const int KeepAliveTimeSeconds = 30;

		private X509CertificateCollection clientCertificates = new X509CertificateCollection();
		private LinkedList<KeyValuePair<byte[], EventHandler>> outputQueue = new LinkedList<KeyValuePair<byte[], EventHandler>>();
		private Dictionary<string, bool> authenticationMechanisms = new Dictionary<string, bool>();
		private Dictionary<string, bool> compressionMethods = new Dictionary<string, bool>();
		private Dictionary<uint, PendingRequest> pendingRequestsBySeqNr = new Dictionary<uint, PendingRequest>();
		private SortedDictionary<DateTime, PendingRequest> pendingRequestsByTimeout = new SortedDictionary<DateTime, PendingRequest>();
		private Dictionary<string, IqEventHandler> iqGetHandlers = new Dictionary<string, IqEventHandler>();
		private Dictionary<string, IqEventHandler> iqSetHandlers = new Dictionary<string, IqEventHandler>();
		private Dictionary<string, MessageEventHandler> messageHandlers = new Dictionary<string, MessageEventHandler>();
		private Dictionary<string, MessageEventArgs> receivedMessages = new Dictionary<string, MessageEventArgs>();
		private Dictionary<string, bool> clientFeatures = new Dictionary<string, bool>();
		private Dictionary<string, RosterItem> roster = new Dictionary<string, RosterItem>();
		private Dictionary<string, int> pendingAssuredMessagesPerSource = new Dictionary<string, int>();
		private byte[] buffer = new byte[BufferSize];
		private AuthenticationMethod authenticationMethod = null;
		private TcpClient client = null;
		private Stream stream = null;
		private Timer secondTimer = null;
		private DateTime nextPing = DateTime.MinValue;
		private UTF8Encoding encoding = new UTF8Encoding(false, false);
		private StringBuilder fragment = new StringBuilder();
		private XmppState state;
		private Random gen = new Random();
		private object synchObject = new object();
		private Availability currentAvailability = Availability.Online;
		private string customPresenceXml = string.Empty;
		private KeyValuePair<string, string>[] customPresenceStatus = new KeyValuePair<string, string>[0];
		private string clientName;
		private string clientVersion;
		private string clientOS;
		private string host;
		private string language;
		private string domain;
		private string bareJid;
		private string fullJid;
		private string resource = string.Empty;
		private string userName;
		private string password;
		private string passwordHash;
		private string passwordHashMethod;
		private string streamId;
		private string streamHeader;
		private string streamFooter;
		private string formSignatureKey;
		private string formSignatureSecret;
		private double version;
		private uint seqnr = 0;
		private int port;
		private int keepAliveSeconds = 30;
		private int inputState = 0;
		private int inputDepth = 0;
		private int defaultRetryTimeout = 2000;
		private int defaultNrRetries = 5;
		private int defaultMaxRetryTimeout = int.MaxValue;
		private int maxAssuredMessagesPendingFromSource = 5;
		private int maxAssuredMessagesPendingTotal = 100;
		private int nrAssuredMessagesPending = 0;
		private bool defaultDropOff = true;
		private bool trustServer = false;
		private bool isWriting = false;
		private bool canRegister = false;
		private bool hasRegistered = false;
		private bool hasRoster = false;
		private bool setPresence = false;
		private bool requestRosterOnStartup = true;
		private bool allowedToRegistered = false;
		private bool allowCramMD5 = true;
		private bool allowDigestMD5 = true;
		private bool allowScramSHA1 = true;
		private bool allowPlain = false;

		/// <summary>
		/// Manages an XMPP client connection. Implements XMPP, as defined in
		/// https://tools.ietf.org/html/rfc6120
		/// https://tools.ietf.org/html/rfc6121
		/// https://tools.ietf.org/html/rfc6122
		/// 
		/// Extensions supported directly by client object:
		/// 
		/// XEP-0030: Service Discovery: http://xmpp.org/extensions/xep-0030.html
		/// XEP-0055: Jabber Search: http://xmpp.org/extensions/xep-0055.html
		/// XEP-0077: In-band Registration: http://xmpp.org/extensions/xep-0077.html
		/// XEP-0092: Software Version: http://xmpp.org/extensions/xep-0092.html
		/// 
		/// Quality of Service: http://xmpp.org/extensions/inbox/qos.html
		/// </summary>
		/// <param name="Host">Host name or IP address of XMPP server.</param>
		/// <param name="Port">Port to connect to.</param>
		/// <param name="Tls">If TLS is used to encrypt communication.</param>
		/// <param name="UserName">User Name</param>
		/// <param name="Password">Password</param>
		/// <param name="Language">Language Code, according to RFC 5646.</param>
		/// <param name="ClientCertificates">Any client certificates.</param>
		public XmppClient(string Host, int Port, string UserName, string Password, string Language, params X509Certificate[] ClientCertificates)
		{
			this.host = this.domain = Host;
			this.port = Port;
			this.userName = UserName;
			this.password = Password;
			this.passwordHash = string.Empty;
			this.passwordHashMethod = string.Empty;
			this.language = Language;
			this.state = XmppState.Connecting;
			this.clientCertificates.AddRange(ClientCertificates);

			this.Init();
		}

		/// <summary>
		/// Manages an XMPP client connection. Implements XMPP, as defined in
		/// https://tools.ietf.org/html/rfc6120
		/// https://tools.ietf.org/html/rfc6121
		/// https://tools.ietf.org/html/rfc6122
		/// 
		/// Extensions supported directly by client object:
		/// 
		/// XEP-0030: Service Discovery: http://xmpp.org/extensions/xep-0030.html
		/// XEP-0055: Jabber Search: http://xmpp.org/extensions/xep-0055.html
		/// XEP-0077: In-band Registration: http://xmpp.org/extensions/xep-0077.html
		/// XEP-0092: Software Version: http://xmpp.org/extensions/xep-0092.html
		/// 
		/// Quality of Service: http://xmpp.org/extensions/inbox/qos.html
		/// </summary>
		/// <param name="Host">Host name or IP address of XMPP server.</param>
		/// <param name="Port">Port to connect to.</param>
		/// <param name="Tls">If TLS is used to encrypt communication.</param>
		/// <param name="UserName">User Name</param>
		/// <param name="PasswordHash">Password hash.</param>
		/// <param name="PasswordHashMethod">Password hash method.</param>
		/// <param name="Language">Language Code, according to RFC 5646.</param>
		/// <param name="ClientCertificates">Any client certificates.</param>
		public XmppClient(string Host, int Port, string UserName, string PasswordHash, string PasswordHashMethod, string Language,
			params X509Certificate[] ClientCertificates)
		{
			this.host = this.domain = Host;
			this.port = Port;
			this.userName = UserName;
			this.password = string.Empty;
			this.passwordHash = PasswordHash;
			this.passwordHashMethod = PasswordHashMethod;
			this.language = Language;
			this.state = XmppState.Connecting;
			this.clientCertificates.AddRange(ClientCertificates);

			this.Init();
		}

		private void Init()
		{
			Assembly ThisAssembly = typeof(XmppClient).Assembly;
			StackTrace Trace = new StackTrace();
			StackFrame Frame;
			MethodBase Method;
			Assembly Assembly;
			int i = 1;
			int c = Trace.FrameCount;

			do
			{
				Frame = Trace.GetFrame(i++);
				Method = Frame.GetMethod();
				Assembly = Method.DeclaringType.Assembly;
			}
			while (Assembly == ThisAssembly);

			AssemblyName Name = Assembly.GetName();
			string Title = string.Empty;
			string Product = string.Empty;
			string AssemblyName = Name.Name;

			foreach (object Attribute in Assembly.GetCustomAttributes(true))
			{
				if (Attribute is AssemblyTitleAttribute)
					Title = ((AssemblyTitleAttribute)Attribute).Title;
				else if (Attribute is AssemblyProductAttribute)
					Product = ((AssemblyProductAttribute)Attribute).Product;
			}

			if (!string.IsNullOrEmpty(Title))
				this.clientName = Title;
			else if (!string.IsNullOrEmpty(Product))
				this.clientName = Product;
			else
				this.clientName = AssemblyName;

			this.clientVersion = Name.Version.ToString();
			this.clientOS = Environment.OSVersion.ToString();

			this.RegisterDefaultHandlers();

			this.client = new TcpClient();
			this.client.BeginConnect(Host, Port, this.ConnectCallback, null);
		}

		private void RegisterDefaultHandlers()
		{
			this.RegisterIqSetHandler("query", NamespaceRoster, this.RosterPushHandler, true);
			this.RegisterIqGetHandler("query", NamespaceServiceDiscoveryInfo, this.ServiceDiscoveryRequestHandler, true);
			this.RegisterIqGetHandler("query", NamespaceSoftwareVersion, this.SoftwareVersionRequestHandler, true);
			this.RegisterIqSetHandler("acknowledged", NamespaceQualityOfService, this.AcknowledgedQoSMessageHandler, true);
			this.RegisterIqSetHandler("assured", NamespaceQualityOfService, this.AssuredQoSMessageHandler, false);
			this.RegisterIqSetHandler("deliver", NamespaceQualityOfService, this.DeliverQoSMessageHandler, false);
			this.RegisterMessageHandler("updated", NamespaceDynamicForms, this.DynamicFormUpdatedHandler, true);

			this.clientFeatures["urn:xmpp:xdata:signature:oauth1"] = true;
			this.clientFeatures["http://jabber.org/protocols/xdata-validate"] = true;
			this.clientFeatures[NamespaceData] = true;
		}

		private void ConnectCallback(IAsyncResult ar)
		{
			try
			{
				this.client.EndConnect(ar);
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);
				return;
			}

			this.stream = new NetworkStream(this.client.Client, false);

			this.State = XmppState.StreamNegotiation;
			this.bareJid = this.fullJid = this.userName + "@" + this.domain;
			this.BeginWrite("<?xml version='1.0'?><stream:stream from='" + XML.Encode(this.bareJid) + "' to='" + XML.Encode(this.domain) +
				"' version='1.0' xml:lang='" + XML.Encode(this.language) + "' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>", null);

			this.ResetState(false);
			this.BeginRead();
		}

		private void ResetState(bool Authenticated)
		{
			this.inputState = 0;
			this.inputDepth = 0;
			this.canRegister = false;
			this.setPresence = false;

			if (!Authenticated)
			{
				this.authenticationMethod = null;
				this.authenticationMechanisms.Clear();
			}

			this.compressionMethods.Clear();
			this.pendingRequestsBySeqNr.Clear();
			this.pendingRequestsByTimeout.Clear();
		}

		private void ConnectionError(Exception ex)
		{
			XmppExceptionEventHandler h = this.OnConnectionError;
			if (h != null)
			{
				try
				{
					h(this, ex);
				}
				catch (Exception ex2)
				{
					Exception(ex2);
				}
			}

			this.Error(ex);

			this.inputState = -1;
			if (this.stream != null)
			{
				this.stream.Close();
				this.stream = null;

				this.client.Close();
				this.client = null;
			}

			this.State = XmppState.Error;
		}

		private void Error(Exception ex)
		{
			this.Error(ex.Message);

			XmppExceptionEventHandler h = this.OnError;
			if (h != null)
			{
				try
				{
					h(this, ex);
				}
				catch (Exception ex2)
				{
					Exception(ex2);
				}
			}
		}

		/// <summary>
		/// Event raised when a connection to a broker could not be made.
		/// </summary>
		public event XmppExceptionEventHandler OnConnectionError = null;

		/// <summary>
		/// Event raised when an error was encountered.
		/// </summary>
		public event XmppExceptionEventHandler OnError = null;

		/// <summary>
		/// Host or IP address of XMPP server.
		/// </summary>
		public string Host
		{
			get { return this.host; }
		}

		/// <summary>
		/// Port number to connect to.
		/// </summary>
		public int Port
		{
			get { return this.port; }
		}

		/// <summary>
		/// If server should be trusted, regardless if the operating system could validate its certificate or not.
		/// </summary>
		public bool TrustServer
		{
			get { return this.trustServer; }
			set { this.trustServer = value; }
		}

		/// <summary>
		/// Current state of connection.
		/// </summary>
		public XmppState State
		{
			get { return this.state; }
			internal set
			{
				if (this.state != value)
				{
					this.state = value;

					this.Information("State changed to " + value.ToString());

					StateChangedEventHandler h = this.OnStateChanged;
					if (h != null)
					{
						try
						{
							h(this, value);
						}
						catch (Exception ex)
						{
							Exception(ex);
						}
					}
				}
			}
		}

		/// <summary>
		/// Event raised whenever the internal state of the connection changes.
		/// </summary>
		public event StateChangedEventHandler OnStateChanged = null;

		/// <summary>
		/// Closes the connection and disposes of all resources.
		/// </summary>
		public void Dispose()
		{
			if (this.state == XmppState.Connected || this.state == XmppState.FetchingRoster || this.state == XmppState.SettingPresence)
				this.BeginWrite(this.streamFooter, this.CleanUp);
			else
				this.CleanUp(this, new EventArgs());
		}

		/// <summary>
		/// Closes the connection the hard way. This might disrupt stream processing, but can simulate a lost connection. To close the connection
		/// softly, call the <see cref="Dispose"/> method.
		/// 
		/// Note: After turning the connection hard-offline, you can reconnect to the server calling the <see cref="Reconnect"/> method.
		/// </summary>
		public void HardOffline()
		{
			this.CleanUp(this, new EventArgs());
		}

		private void CleanUp(object Sender, EventArgs e)
		{
			this.State = XmppState.Offline;

			if (this.outputQueue != null)
			{
				lock (this.outputQueue)
				{
					this.outputQueue.Clear();
				}
			}

			if (this.authenticationMechanisms != null)
				this.authenticationMechanisms.Clear();

			if (this.compressionMethods != null)
				this.compressionMethods.Clear();

			if (this.pendingRequestsBySeqNr != null)
			{
				lock (this.synchObject)
				{
					this.pendingRequestsBySeqNr.Clear();
					this.pendingRequestsByTimeout.Clear();
				}
			}

			if (this.secondTimer != null)
			{
				this.secondTimer.Dispose();
				this.secondTimer = null;
			}

			if (this.stream != null)
			{
				this.stream.Dispose();
				this.stream = null;
			}

			if (this.client != null)
			{
				this.client.Close();
				this.client = null;
			}
		}

		/// <summary>
		/// Reconnects a client after an error or if it's offline. Reconnecting, instead of creating a completely new connection,
		/// saves time. It binds to the same resource provided earlier, and avoids fetching the roster.
		/// </summary>
		public void Reconnect()
		{
			if (this.client != null)
			{
				this.client.Close();
				this.client = null;
			}

			this.state = XmppState.Connecting;
			this.client = new TcpClient();
			this.client.BeginConnect(Host, Port, this.ConnectCallback, null);
		}

		private void BeginWrite(string Xml, EventHandler Callback)
		{
			TransmitText(Xml);

			byte[] Packet = this.encoding.GetBytes(Xml);

			lock (this.outputQueue)
			{
				if (this.isWriting)
					this.outputQueue.AddLast(new KeyValuePair<byte[], EventHandler>(Packet, Callback));
				else
					this.DoBeginWriteLocked(Packet, Callback);
			}
		}

		private void DoBeginWriteLocked(byte[] Packet, EventHandler Callback)
		{
			this.stream.BeginWrite(Packet, 0, Packet.Length, this.EndWrite, Callback);
			this.isWriting = true;
		}

		private void EndWrite(IAsyncResult ar)
		{
			if (this.stream == null)
				return;

			try
			{
				this.stream.EndWrite(ar);
				this.nextPing = DateTime.Now.AddMilliseconds(this.keepAliveSeconds * 500);

				EventHandler h = (EventHandler)ar.AsyncState;
				if (h != null)
				{
					try
					{
						h(this, new EventArgs());
					}
					catch (Exception ex)
					{
						Exception(ex);
					}
				}

				lock (this.outputQueue)
				{
					LinkedListNode<KeyValuePair<byte[], EventHandler>> Next = this.outputQueue.First;

					if (Next == null)
						this.isWriting = false;
					else
					{
						this.outputQueue.RemoveFirst();
						this.DoBeginWriteLocked(Next.Value.Key, Next.Value.Value);
					}
				}
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);

				lock (this.outputQueue)
				{
					this.outputQueue.Clear();
					this.isWriting = false;
				}
			}
		}

		private void BeginRead()
		{
			this.stream.BeginRead(this.buffer, 0, BufferSize, this.EndRead, null);
		}

		private void EndRead(IAsyncResult ar)
		{
			string s;
			int NrRead;

			if (this.stream == null)
				return;

			try
			{
				NrRead = this.stream.EndRead(ar);
				if (NrRead > 0)
				{
					s = this.encoding.GetString(this.buffer, 0, NrRead);
					ReceiveText(s);

					if (this.ParseIncoming(s))
						this.stream.BeginRead(this.buffer, 0, BufferSize, this.EndRead, null);
				}
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);
				return;
			}
		}

		private bool ParseIncoming(string s)
		{
			bool Result = true;

			foreach (char ch in s)
			{
				switch (this.inputState)
				{
					case 0:		// Waiting for <?
						if (ch == '<')
						{
							this.fragment.Append(ch);
							this.inputState++;
						}
						else if (ch > ' ')
						{
							this.inputState = -1;
							if (this.stream != null)
							{
								this.stream.Close();
								this.stream = null;

								this.client.Close();
								this.client = null;
							}
							this.State = XmppState.Error;
							return false;
						}
						break;

					case 1:		// Waiting for ? or >
						this.fragment.Append(ch);
						if (ch == '?')
							this.inputState++;
						else if (ch == '>')
						{
							this.inputState = 5;
							this.inputDepth = 1;
							this.ProcessStream(this.fragment.ToString());
							this.fragment.Clear();
						}
						break;

					case 2:		// Waiting for ?>
						this.fragment.Append(ch);
						if (ch == '>')
							this.inputState++;
						break;

					case 3:		// Waiting for <stream
						this.fragment.Append(ch);
						if (ch == '<')
							this.inputState++;
						else if (ch > ' ')
						{
							this.inputState = -1;
							if (this.stream != null)
							{
								this.stream.Close();
								this.stream = null;

								this.client.Close();
								this.client = null;
							}
							this.State = XmppState.Error;
							return false;
						}
						break;

					case 4:		// Waiting for >
						this.fragment.Append(ch);
						if (ch == '>')
						{
							this.inputState++;
							this.inputDepth = 1;
							this.ProcessStream(this.fragment.ToString());
							this.fragment.Clear();
						}
						break;

					case 5:	// Waiting for <
						if (ch == '<')
						{
							this.fragment.Append(ch);
							this.inputState++;
						}

						else if (this.inputDepth > 1)
							this.fragment.Append(ch);
						else if (ch > ' ')
						{
							this.inputState = -1;
							if (this.stream != null)
							{
								this.stream.Close();
								this.stream = null;

								this.client.Close();
								this.client = null;
							}
							this.State = XmppState.Error;
							return false;
						}
						break;

					case 6:	// Second character in tag
						this.fragment.Append(ch);
						if (ch == '/')
							this.inputState++;
						else
							this.inputState += 2;
						break;

					case 7:	// Waiting for end of closing tag
						this.fragment.Append(ch);
						if (ch == '>')
						{
							this.inputDepth--;
							if (this.inputDepth < 1)
							{
								this.inputState = -1;
								if (this.stream != null)
								{
									this.stream.Close();
									this.stream = null;

									this.client.Close();
									this.client = null;
								}
								this.State = XmppState.Offline;
								return false;
							}
							else
							{
								if (this.inputDepth == 1)
								{
									if (!this.ProcessFragment(this.fragment.ToString()))
										Result = false;

									this.fragment.Clear();
								}

								if (this.inputState > 0)
									this.inputState = 5;
							}
						}
						break;

					case 8:	// Wait for end of start tag
						this.fragment.Append(ch);
						if (ch == '>')
						{
							this.inputDepth++;
							this.inputState = 5;
						}
						else if (ch == '/')
							this.inputState++;
						break;

					case 9:	// Check for end of childless tag.
						this.fragment.Append(ch);
						if (ch == '>')
						{
							if (this.inputDepth == 1)
							{
								if (!this.ProcessFragment(this.fragment.ToString()))
									Result = false;

								this.fragment.Clear();
							}

							if (this.inputState != 0)
								this.inputState = 5;
						}
						else
							this.inputState--;
						break;

					default:
						break;
				}
			}

			return Result;
		}

		private void ProcessStream(string Xml)
		{
			try
			{
				int i = Xml.IndexOf("?>");
				if (i >= 0)
					Xml = Xml.Substring(i + 2).TrimStart();

				this.streamHeader = Xml;

				i = Xml.IndexOf(":stream");
				if (i < 0)
					this.streamFooter = "</stream>";
				else
					this.streamFooter = "</" + Xml.Substring(1, i - 1) + ":stream>";

				XmlDocument Doc = new XmlDocument();
				Doc.LoadXml(Xml + this.streamFooter);

				if (Doc.DocumentElement.LocalName != "stream")
					throw new XmppException("Invalid stream.", Doc.DocumentElement);

				XmlElement Stream = Doc.DocumentElement;

				this.version = XML.Attribute(Stream, "version", 0.0);
				this.streamId = XML.Attribute(Stream, "id");
				this.domain = XML.Attribute(Stream, "from");
				this.bareJid = this.fullJid = this.userName + "@" + this.domain;

				if (this.version < 1.0)
					throw new XmppException("Version not supported.", Stream);
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);
			}
		}

		private bool ProcessFragment(string Xml)
		{
			XmlDocument Doc;
			XmlElement E;

			try
			{
				Doc = new XmlDocument();
				Doc.LoadXml(this.streamHeader + Xml + this.streamFooter);

				foreach (XmlNode N in Doc.DocumentElement.ChildNodes)
				{
					E = N as XmlElement;
					if (E == null)
						continue;

					switch (E.LocalName)
					{
						case "iq":
							string Type = XML.Attribute(E, "type");
							string Id = XML.Attribute(E, "id");
							string To = XML.Attribute(E, "to");
							string From = XML.Attribute(E, "from");
							switch (Type)
							{
								case "get":
									this.ProcessIq(this.iqGetHandlers, new IqEventArgs(this, E, Id, To, From));
									break;

								case "set":
									this.ProcessIq(this.iqSetHandlers, new IqEventArgs(this, E, Id, To, From));
									break;

								case "result":
								case "error":
									uint SeqNr;
									IqResultEventHandler Callback;
									object State;
									PendingRequest Rec;
									bool Ok = (Type == "result");

									if (uint.TryParse(Id, out SeqNr))
									{
										lock (this.synchObject)
										{
											if (this.pendingRequestsBySeqNr.TryGetValue(SeqNr, out Rec))
											{
												Callback = Rec.Callback;
												State = Rec.State;

												this.pendingRequestsBySeqNr.Remove(SeqNr);
												this.pendingRequestsByTimeout.Remove(Rec.Timeout);
											}
											else
											{
												Callback = null;
												State = null;
											}
										}

										if (Callback != null)
										{
											try
											{
												Callback(this, new IqResultEventArgs(E, Id, To, From, Ok, State));
											}
											catch (Exception ex)
											{
												Exception(ex);
											}
										}
									}
									break;
							}
							break;

						case "message":
							this.ProcessMessage(new MessageEventArgs(this, E));
							break;

						case "presence":
							this.ProcessPresence(new PresenceEventArgs(this, E));
							break;

						case "features":
							if (E.FirstChild == null)
								this.AdvanceUntilConnected();
							else
							{
								bool StartTls = false;
								bool Auth = false;
								bool Bind = false;

								foreach (XmlNode N2 in E.ChildNodes)
								{
									switch (N2.LocalName)
									{
										case "starttls":
											StartTls = true;
											break;

										case "mechanisms":
											foreach (XmlNode N3 in N2.ChildNodes)
											{
												if (N3.LocalName == "mechanism")
													this.authenticationMechanisms[N3.InnerText.Trim().ToUpper()] = true;
											}
											break;

										case "compression":
											foreach (XmlNode N3 in N2.ChildNodes)
											{
												if (N3.LocalName == "method")
													this.compressionMethods[N3.InnerText.Trim().ToUpper()] = true;
											}
											break;

										case "auth":
											Auth = true;
											break;

										case "register":
											this.canRegister = true;
											break;

										case "bind":
											Bind = true;
											break;

										case "session":
											break;

										default:
											break;
									}
								}

								if (StartTls)
								{
									this.BeginWrite("<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>", null);
									return true;
								}
								else if (Auth)
								{
									this.StartAuthentication();
									return true;
								}
								else if (Bind)
								{
									this.State = XmppState.Binding;
									if (string.IsNullOrEmpty(this.resource))
										this.SendIqSet(this.domain, "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/>", this.BindResult, null);
									else
									{
										this.SendIqSet(this.domain, "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'><resource>" +
											XML.Encode(this.resource) + "</resource></bind>", this.BindResult, null);
									}
									return true;
								}
								else if (this.authenticationMechanisms.Count > 0 && this.state == XmppState.Connecting || this.state == XmppState.StartingEncryption)
								{
									this.StartAuthentication();
									return true;
								}
							}
							break;

						case "proceed":
							this.State = XmppState.StartingEncryption;

							SslStream SslStream = new SslStream(this.stream, true, this.ValidateCertificate);
							this.stream = SslStream;

							SslStream.BeginAuthenticateAsClient(this.host, this.clientCertificates, SslProtocols.Tls, true, this.EndAuthenticateAsClient, null);
							return false;

						case "failure":
							if (this.authenticationMethod != null)
							{
								if (this.canRegister && !this.hasRegistered && this.allowedToRegistered && !string.IsNullOrEmpty(this.password))
								{
									this.hasRegistered = true;
									this.SendIqGet(this.domain, "<query xmlns='" + NamespaceRegister + "'/>", this.RegistrationFormReceived, null);
									break;
								}
								else if (E.FirstChild == null)
									throw new XmppException("Unable to authenticate user.", E);
								else
									throw GetSaslExceptionObject(E);
							}
							else
							{
								if (E.FirstChild == null)
									throw new XmppException("Unable to start TLS negotiation.", E);
								else
									throw GetStreamExceptionObject(E);
							}

						case "challenge":
							if (this.authenticationMethod == null)
								throw new XmppException("No authentication method selected.", E);
							else
							{
								string Response = this.authenticationMethod.Challenge(E.InnerText, this);
								this.BeginWrite("<response xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" + Response + "</response>", null);
							}
							break;

						case "error":
							XmppException StreamException = GetStreamExceptionObject(E);
							if (StreamException is SeeOtherHostException)
							{
								this.host = ((SeeOtherHostException)StreamException).NewHost;
								this.inputState = -1;

								this.Information("Reconnecting to " + this.host);

								if (this.stream != null)
								{
									this.stream.Dispose();
									this.stream = null;
								}

								if (this.client != null)
								{
									this.client.Close();
									this.client = null;
								}

								this.client = new TcpClient();
								this.client.BeginConnect(Host, Port, this.ConnectCallback, null);
								return false;
							}
							else
								throw StreamException;

						case "success":
							if (this.authenticationMethod == null)
								throw new XmppException("No authentication method selected.", E);
							else
							{
								if (this.authenticationMethod.CheckSuccess(E.InnerText, this))
								{
									this.ResetState(true);
									this.BeginWrite("<?xml version='1.0'?><stream:stream from='" + XML.Encode(this.bareJid) + "' to='" + XML.Encode(this.domain) +
										"' version='1.0' xml:lang='" + XML.Encode(this.language) + "' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>", null);
								}
								else
									throw new XmppException("Server authentication rejected by client.", E);
							}
							break;

						default:
							break;
					}
				}
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);
				return false;
			}

			return true;
		}

		private void ProcessMessage(MessageEventArgs e)
		{
			MessageEventHandler h = null;
			string Key;

			lock (this.synchObject)
			{
				foreach (XmlElement E in e.Message.ChildNodes)
				{
					Key = E.LocalName + " " + E.NamespaceURI;
					if (this.messageHandlers.TryGetValue(Key, out h))
					{
						e.Content = E;
						break;
					}
					else
						h = null;
				}
			}

			if (h != null)
				this.Information(h.Method.Name);
			else
			{
				switch (e.Type)
				{
					case MessageType.Chat:
						this.Information("OnChatMessage()");
						h = this.OnChatMessage;
						break;

					case MessageType.Error:
						this.Information("OnErrorMessage()");
						h = this.OnErrorMessage;
						break;

					case MessageType.GroupChat:
						this.Information("OnGroupChatMessage()");
						h = this.OnGroupChatMessage;
						break;

					case MessageType.Headline:
						this.Information("OnHeadlineMessage()");
						h = this.OnHeadlineMessage;
						break;

					case MessageType.Normal:
					default:
						this.Information("OnNormalMessage()");
						h = this.OnNormalMessage;
						break;
				}
			}

			if (h != null)
			{
				try
				{
					h(this, e);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		private void ProcessPresence(PresenceEventArgs e)
		{
			PresenceEventHandler h;
			RosterItem Item;

			switch (e.Type)
			{
				case PresenceType.Available:
					this.Information("OnPresence()");
					h = this.OnPresence;

					lock (this.roster)
					{
						if (this.roster.TryGetValue(e.FromBareJID, out Item))
							Item.LastPresence = e;
					}
					break;

				case PresenceType.Unavailable:
					this.Information("OnPresence()");
					h = this.OnPresence;

					lock (this.roster)
					{
						if (this.roster.TryGetValue(e.FromBareJID, out Item))
						{
							if (Item.LastPresenceFullJid == e.From)
								Item.LastPresence = null;
						}
					}
					break;

				case PresenceType.Error:
				case PresenceType.Probe:
				default:
					this.Information("OnPresence()");
					h = this.OnPresence;
					break;

				case PresenceType.Subscribe:
					this.Information("OnPresenceSubscribe()");
					h = this.OnPresenceSubscribe;
					break;

				case PresenceType.Subscribed:
					this.Information("OnPresenceSubscribed()");
					h = this.OnPresenceSubscribed;
					break;

				case PresenceType.Unsubscribe:
					this.Information("OnPresenceUnsubscribe()");
					h = this.OnPresenceUnsubscribe;
					break;

				case PresenceType.Unsubscribed:
					this.Information("OnPresenceUnsubscribed()");
					h = this.OnPresenceUnsubscribed;
					break;
			}

			if (h != null)
			{
				try
				{
					h(this, e);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		private void ProcessIq(Dictionary<string, IqEventHandler> Handlers, IqEventArgs e)
		{
			IqEventHandler h = null;
			string Key;

			lock (this.synchObject)
			{
				foreach (XmlElement E in e.IQ.ChildNodes)
				{
					Key = E.LocalName + " " + E.NamespaceURI;
					if (Handlers.TryGetValue(Key, out h))
					{
						e.Query = E;
						break;
					}
					else
						h = null;
				}
			}

			if (h == null)
				this.SendIqError(e.Id, e.From, "<error type='cancel'><feature-not-implemented xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>");
			else
			{
				try
				{
					h(this, e);
				}
				catch (StanzaExceptionException ex)
				{
					StringBuilder Xml = new StringBuilder();

					this.Error(ex.Message);

					Xml.Append("<error type='");
					Xml.Append(ex.ErrorType);
					Xml.Append("'><");
					Xml.Append(ex.ErrorStanzaName);
					Xml.Append(" xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>");
					Xml.Append("<text>");
					Xml.Append(XML.Encode(ex.Message));
					Xml.Append("</text>");
					Xml.Append("</error>");

					this.SendIqError(e.Id, e.From, Xml.ToString());
				}
				catch (Exception ex)
				{
					StringBuilder Xml = new StringBuilder();

					this.Exception(ex);

					Xml.Append("<error type='cancel'><internal-server-error xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>");
					Xml.Append("<text>");
					Xml.Append(XML.Encode(ex.Message));
					Xml.Append("</text>");
					Xml.Append("</error>");

					this.SendIqError(e.Id, e.From, Xml.ToString());
				}
			}
		}

		/// <summary>
		/// Registers an IQ-Get handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to process request.</param>
		/// <param name="PublishNamespaceAsClientFeature">If the namespace should be published as a client feature.</param>
		public void RegisterIqGetHandler(string LocalName, string Namespace, IqEventHandler Handler, bool PublishNamespaceAsClientFeature)
		{
			this.RegisterIqHandler(this.iqGetHandlers, LocalName, Namespace, Handler, PublishNamespaceAsClientFeature);
		}

		/// <summary>
		/// Registers an IQ-Set handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to process request.</param>
		/// <param name="PublishNamespaceAsClientFeature">If the namespace should be published as a client feature.</param>
		public void RegisterIqSetHandler(string LocalName, string Namespace, IqEventHandler Handler, bool PublishNamespaceAsClientFeature)
		{
			this.RegisterIqHandler(this.iqSetHandlers, LocalName, Namespace, Handler, PublishNamespaceAsClientFeature);
		}

		private void RegisterIqHandler(Dictionary<string, IqEventHandler> Handlers, string LocalName, string Namespace, IqEventHandler Handler,
			bool PublishNamespaceAsClientFeature)
		{
			string Key = LocalName + " " + Namespace;

			lock (this.synchObject)
			{
				if (Handlers.ContainsKey(Key))
					throw new ArgumentException("Handler already registered.", "LocalName");

				Handlers[Key] = Handler;

				if (PublishNamespaceAsClientFeature)
					this.clientFeatures[Namespace] = true;
			}
		}

		/// <summary>
		/// Unregisters an IQ-Get handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to process request.</param>
		/// <param name="RemoveNamespaceAsClientFeature">If the namespace should be removed from the lit of client features.</param>
		/// <returns>If the handler was found and removed.</returns>
		public bool UnregisterIqGetHandler(string LocalName, string Namespace, IqEventHandler Handler, bool RemoveNamespaceAsClientFeature)
		{
			return this.UnregisterIqHandler(this.iqGetHandlers, LocalName, Namespace, Handler, RemoveNamespaceAsClientFeature);
		}

		/// <summary>
		/// Unregisters an IQ-Set handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to process request.</param>
		/// <param name="RemoveNamespaceAsClientFeature">If the namespace should be removed from the lit of client features.</param>
		/// <returns>If the handler was found and removed.</returns>
		public bool UnregisterIqSetHandler(string LocalName, string Namespace, IqEventHandler Handler, bool RemoveNamespaceAsClientFeature)
		{
			return this.UnregisterIqHandler(this.iqSetHandlers, LocalName, Namespace, Handler, RemoveNamespaceAsClientFeature);
		}

		private bool UnregisterIqHandler(Dictionary<string, IqEventHandler> Handlers, string LocalName, string Namespace, IqEventHandler Handler,
			bool RemoveNamespaceAsClientFeature)
		{
			IqEventHandler h;
			string Key = LocalName + " " + Namespace;

			lock (this.synchObject)
			{
				if (!Handlers.TryGetValue(Key, out h))
					return false;

				if (h != Handler)
					return false;

				Handlers.Remove(Key);

				if (RemoveNamespaceAsClientFeature)
					this.clientFeatures.Remove(Namespace);
			}

			return true;
		}

		/// <summary>
		/// Registers a Message handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to process message.</param>
		/// <param name="PublishNamespaceAsClientFeature">If the namespace should be published as a client feature.</param>
		public void RegisterMessageHandler(string LocalName, string Namespace, MessageEventHandler Handler, bool PublishNamespaceAsClientFeature)
		{
			string Key = LocalName + " " + Namespace;

			lock (this.synchObject)
			{
				if (this.messageHandlers.ContainsKey(Key))
					throw new ArgumentException("Handler already registered.", "LocalName");

				this.messageHandlers[Key] = Handler;

				if (PublishNamespaceAsClientFeature)
					this.clientFeatures[Namespace] = true;
			}
		}

		/// <summary>
		/// Unregisters a Message handler.
		/// </summary>
		/// <param name="LocalName">Local Name</param>
		/// <param name="Namespace">Namespace</param>
		/// <param name="Handler">Handler to remove.</param>
		/// <param name="RemoveNamespaceAsClientFeature">If the namespace should be removed from the lit of client features.</param>
		/// <returns>If the handler was found and removed.</returns>
		public bool UnregisterMessageHandler(string LocalName, string Namespace, MessageEventHandler Handler, bool RemoveNamespaceAsClientFeature)
		{
			MessageEventHandler h;
			string Key = LocalName + " " + Namespace;

			lock (this.synchObject)
			{
				if (!this.messageHandlers.TryGetValue(Key, out h))
					return false;

				if (h != Handler)
					return false;

				this.messageHandlers.Remove(Key);

				if (RemoveNamespaceAsClientFeature)
					this.clientFeatures.Remove(Namespace);
			}

			return true;
		}

		/// <summary>
		/// Registers a feature on the client.
		/// </summary>
		/// <param name="Feature">Feature to register.</param>
		public void RegisterFeature(string Feature)
		{
			lock (this.synchObject)
			{
				this.clientFeatures[Feature] = true;
			}
		}

		/// <summary>
		/// Unregisters a feature from the client.
		/// </summary>
		/// <param name="Feature">Feature to remove.</param>
		/// <returns>If the feature was found and removed.</returns>
		public bool UnregisterFeature(string Feature)
		{
			lock (this.synchObject)
			{
				return this.clientFeatures.Remove(Feature);
			}
		}

		/// <summary>
		/// Event raised when a presence message has been received from a resource.
		/// </summary>
		public event PresenceEventHandler OnPresence = null;

		/// <summary>
		/// Event raised when a resource is requesting to be informed of the current client's presence
		/// </summary>
		public event PresenceEventHandler OnPresenceSubscribe = null;

		/// <summary>
		/// Event raised when your presence subscription has been accepted.
		/// </summary>
		public event PresenceEventHandler OnPresenceSubscribed = null;

		/// <summary>
		/// Event raised when a resource is requesting to be removed from the current client's presence
		/// </summary>
		public event PresenceEventHandler OnPresenceUnsubscribe = null;

		/// <summary>
		/// Event raised when your presence unsubscription has been accepted.
		/// </summary>
		public event PresenceEventHandler OnPresenceUnsubscribed = null;

		/// <summary>
		/// Raised when a chat message has been received, that is not handled by a specific message handler.
		/// </summary>
		public event MessageEventHandler OnChatMessage = null;

		/// <summary>
		/// Raised when an error message has been received, that is not handled by a specific message handler.
		/// </summary>
		public event MessageEventHandler OnErrorMessage = null;

		/// <summary>
		/// Raised when a group chat message has been received, that is not handled by a specific message handler.
		/// </summary>
		public event MessageEventHandler OnGroupChatMessage = null;

		/// <summary>
		/// Raised when a headline message has been received, that is not handled by a specific message handler.
		/// </summary>
		public event MessageEventHandler OnHeadlineMessage = null;

		/// <summary>
		/// Raised when a normal message has been received, that is not handled by a specific message handler.
		/// </summary>
		public event MessageEventHandler OnNormalMessage = null;

		private void StartAuthentication()
		{
			if (this.authenticationMethod == null)
			{
				if (this.allowScramSHA1 && this.authenticationMechanisms.ContainsKey("SCRAM-SHA-1") &&
					(string.IsNullOrEmpty(this.passwordHashMethod) || this.passwordHashMethod == "SCRAM-SHA-1"))
				{
					string Nonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray(), Base64FormattingOptions.None);
					string s = "n,,n=" + this.userName + ",r=" + Nonce;
					byte[] Data = System.Text.Encoding.UTF8.GetBytes(s);

					this.State = XmppState.Authenticating;
					this.authenticationMethod = new ScramSha1(Nonce);
					this.BeginWrite("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='SCRAM-SHA-1'>" +
						Convert.ToBase64String(Data) + "</auth>", null);
				}
				else if (this.allowDigestMD5 && this.authenticationMechanisms.ContainsKey("DIGEST-MD5") &&
					(string.IsNullOrEmpty(this.passwordHashMethod) || this.passwordHashMethod == "DIGEST-MD5"))
				{
					this.State = XmppState.Authenticating;
					this.authenticationMethod = new DigestMd5();
					this.BeginWrite("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='DIGEST-MD5'/>", null);
				}
				else if (this.allowCramMD5 && this.authenticationMechanisms.ContainsKey("CRAM-MD5") &&
					(string.IsNullOrEmpty(this.passwordHashMethod) || this.passwordHashMethod == "CRAM-MD5"))
				{
					this.State = XmppState.Authenticating;
					this.authenticationMethod = new CramMd5();
					this.BeginWrite("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='CRAM-MD5'/>", null);
				}
				else if (this.allowPlain && this.authenticationMechanisms.ContainsKey("PLAIN") &&
					(string.IsNullOrEmpty(this.passwordHashMethod) || this.passwordHashMethod == "PLAIN"))
				{
					this.State = XmppState.Authenticating;
					this.authenticationMethod = new Plain();

					string Pwd;

					if (string.IsNullOrEmpty(this.passwordHashMethod))
					{
						Pwd = this.password;
						this.passwordHash = Pwd;
						this.passwordHashMethod = "PLAIN";
					}
					else
						Pwd = this.passwordHash;

					this.BeginWrite("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>" +
						Convert.ToBase64String(this.encoding.GetBytes("\x00" + this.userName + "\x00" + Pwd)) + "</auth>", null);
				}
				//else if (this.authenticationMechanisms.ContainsKey("ANONYMOUS"))
				//	throw new XmppException("ANONYMOUS authentication method not allowed.");
				else
					throw new XmppException("No allowed authentication method supported.");
			}
		}

		internal static XmppException GetStreamExceptionObject(XmlElement E)
		{
			string Msg = string.Empty;

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.LocalName == "text" && N2.NamespaceURI == NamespaceXmppStreams)
					Msg = N2.InnerText.Trim();
			}

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.NamespaceURI == NamespaceXmppStreams)
				{
					switch (N2.LocalName)
					{
						// Stream Exceptions:
						case "bad-format": return new BadFormatException(Msg, E);
						case "bad-namespace-prefix": return new BadNamespacePrefixException(Msg, E);
						case "conflict": return new StreamErrors.ConflictException(Msg, E);
						case "connection-timeout": return new ConnectionTimeoutException(Msg, E);
						case "host-gone": return new HostGoneException(Msg, E);
						case "host-unknown": return new HostUnknownException(Msg, E);
						case "improper-addressing": return new ImproperAddressingException(Msg, E);
						case "internal-server-error": return new StreamErrors.InternalServerErrorException(Msg, E);
						case "invalid-from": return new InvalidFromException(Msg, E);
						case "invalid-namespace": return new InvalidNamespaceException(Msg, E);
						case "invalid-xml": return new InvalidXmlException(Msg, E);
						case "not-authorized": return new StreamErrors.NotAuthorizedException(Msg, E);
						case "not-well-formed": return new NotWellFormedException(Msg, E);
						case "policy-violation": return new StreamErrors.PolicyViolationException(Msg, E);
						case "remote-connection-failed": return new RemoteConnectionFailedException(Msg, E);
						case "reset": return new ResetException(Msg, E);
						case "resource-constraint": return new StreamErrors.ResourceConstraintException(Msg, E);
						case "restricted-xml": return new RestrictedXmlException(Msg, E);
						case "see-other-host": return new SeeOtherHostException(Msg, E, N2.InnerText);
						case "system-shutdown": return new SystemShutdownException(Msg, E);
						case "undefined-condition": return new StreamErrors.UndefinedConditionException(Msg, E);
						case "unsupported-encoding": return new UnsupportedEncodingException(Msg, E);
						case "unsupported-feature": return new UnsupportedFeatureException(Msg, E);
						case "unsupported-stanza-type": return new UnsupportedStanzaTypeException(Msg, E);
						case "unsupported-version": return new UnsupportedVersionException(Msg, E);
						default: return new XmppException(string.IsNullOrEmpty(Msg) ? "Unrecognized stream error returned." : Msg, E);
					}
				}
			}

			return new XmppException(string.IsNullOrEmpty(Msg) ? "Unspecified error returned." : Msg, E);
		}

		internal static XmppException GetStanzaExceptionObject(XmlElement E)
		{
			string Msg = string.Empty;

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.LocalName == "text" && N2.NamespaceURI == NamespaceXmppStanzas)
					Msg = N2.InnerText.Trim();
			}

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.NamespaceURI == NamespaceXmppStanzas)
				{
					switch (N2.LocalName)
					{
						case "bad-request": return new BadRequestException(Msg, E);
						case "conflict": return new StanzaErrors.ConflictException(Msg, E);
						case "feature-not-implemented": return new FeatureNotImplementedException(Msg, E);
						case "forbidden": return new ForbiddenException(Msg, E);
						case "gone": return new GoneException(Msg, E);
						case "internal-server-error": return new StanzaErrors.InternalServerErrorException(Msg, E);
						case "item-not-found": return new ItemNotFoundException(Msg, E);
						case "jid-malformed": return new JidMalformedException(Msg, E);
						case "not-acceptable": return new NotAcceptableException(Msg, E);
						case "not-allowed": return new NotAllowedException(Msg, E);
						case "not-authorized": return new StanzaErrors.NotAuthorizedException(Msg, E);
						case "policy-violation": return new StanzaErrors.PolicyViolationException(Msg, E);
						case "recipient-unavailable": return new RecipientUnavailableException(Msg, E);
						case "redirect": return new RedirectException(Msg, E);
						case "registration-required": return new RegistrationRequiredException(Msg, E);
						case "remote-server-not-found": return new RemoteServerNotFoundException(Msg, E);
						case "remote-server-timeout": return new RemoteServerTimeoutException(Msg, E);
						case "resource-constraint": return new StanzaErrors.ResourceConstraintException(Msg, E);
						case "service-unavailable": return new ServiceUnavailableException(Msg, E);
						case "subscription-required": return new SubscriptionRequiredException(Msg, E);
						case "undefined-condition": return new StanzaErrors.UndefinedConditionException(Msg, E);
						case "unexpected-request": return new UnexpectedRequestException(Msg, E);
						default: return new XmppException(string.IsNullOrEmpty(Msg) ? "Unrecognized stanza error returned." : string.Empty, E);
					}
				}
			}

			return new XmppException(string.IsNullOrEmpty(Msg) ? "Unspecified error returned." : string.Empty, E);
		}

		internal static XmppException GetSaslExceptionObject(XmlElement E)
		{
			string Msg = string.Empty;

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.LocalName == "text" && N2.NamespaceURI == NamespaceXmppStreams)
					Msg = N2.InnerText.Trim();
			}

			foreach (XmlNode N2 in E.ChildNodes)
			{
				if (N2.NamespaceURI == NamespaceXmppSasl)
				{
					switch (N2.LocalName)
					{
						case "account-disabled": return new AccountDisabledException(Msg, E);
						case "credentials-expired": return new CredentialsExpiredException(Msg, E);
						case "encryption-required": return new EncryptionRequiredException(Msg, E);
						case "incorrect-encoding": return new IncorrectEncodingException(Msg, E);
						case "invalid-authzid": return new InvalidAuthzidException(Msg, E);
						case "invalid-mechanism": return new InvalidMechanismException(Msg, E);
						case "malformed-request": return new MalformedRequestException(Msg, E);
						case "mechanism-too-weak": return new MechanismTooWeakException(Msg, E);
						case "not-authorized": return new AuthenticationErrors.NotAuthorizedException(Msg, E);
						case "temporary-auth-failure": return new TemporaryAuthFailureException(Msg, E);
						default: return new XmppException(string.IsNullOrEmpty(Msg) ? "Unrecognized SASL error returned." : Msg, E);
					}
				}
			}

			return new XmppException(string.IsNullOrEmpty(Msg) ? "Unspecified error returned." : Msg, E);
		}

		private bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			if (sslPolicyErrors != SslPolicyErrors.None)
				return this.trustServer;

			return true;
		}

		private void EndAuthenticateAsClient(IAsyncResult ar)
		{
			try
			{
				if (this.stream != null)
				{
					((SslStream)this.stream).EndAuthenticateAsClient(ar);

					this.BeginWrite("<?xml version='1.0'?><stream:stream from='" + XML.Encode(this.bareJid) + "' to='" + XML.Encode(this.domain) +
						"' version='1.0' xml:lang='" + XML.Encode(this.language) + "' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams'>", null);

					this.ResetState(false);
					this.BeginRead();
				}
			}
			catch (Exception ex)
			{
				this.ConnectionError(ex);
			}
		}

		internal string UserName
		{
			get { return this.userName; }
		}

		internal string Password
		{
			get { return this.password; }
		}

		/// <summary>
		/// Hash value of password. Depends on method used to authenticate user.
		/// </summary>
		public string PasswordHash
		{
			get { return this.passwordHash; }
			internal set { this.passwordHash = value; }
		}

		/// <summary>
		/// Password hash method.
		/// </summary>
		public string PasswordHashMethod
		{
			get { return this.passwordHashMethod; }
			internal set { this.passwordHashMethod = value; }
		}

		/// <summary>
		/// Current Domain.
		/// </summary>
		public string Domain
		{
			get { return this.domain; }
		}

		/// <summary>
		/// Bare JID
		/// </summary>
		public string BareJID
		{
			get { return this.bareJid; }
		}

		/// <summary>
		/// Full JID.
		/// </summary>
		public string FullJID
		{
			get { return this.fullJid; }
		}

		/// <summary>
		/// Resource part of the <see cref="FullJID"/>. Will be available after successfully binding the connection.
		/// </summary>
		public string Resource
		{
			get { return this.resource; }
		}

		/// <summary>
		/// If the CRAM-MD5 authentication method is allowed or not. Default is true.
		/// </summary>
		public bool AllowCramMD5
		{
			get { return this.allowCramMD5; }
			set { this.allowCramMD5 = value; }
		}

		/// <summary>
		/// If the DIGEST-MD5 authentication method is allowed or not. Default is true.
		/// </summary>
		public bool AllowDigestMD5
		{
			get { return this.allowDigestMD5; }
			set { this.allowDigestMD5 = value; }
		}

		/// <summary>
		/// If the SCRAM-SHA-1 authentication method is allowed or not. Default is true.
		/// </summary>
		public bool AllowScramSHA1
		{
			get { return this.allowScramSHA1; }
			set { this.allowScramSHA1 = value; }
		}

		/// <summary>
		/// If the PLAIN authentication method is allowed or not. Default is true.
		/// </summary>
		public bool AllowPlain
		{
			get { return this.allowPlain; }
			set { this.allowPlain = value; }
		}

		/// <summary>
		/// If registration of a new account is allowed. 
		/// Requires a password. Having a password hash is not sufficient.
		/// </summary>
		public void AllowRegistration()
		{
			this.AllowRegistration(string.Empty, string.Empty);
		}

		/// <summary>
		/// If registration of a new account is allowed.
		/// Requires a password. Having a password hash is not sufficient.
		/// </summary>
		/// <param name="FormSignatureKey">Form signature key, if form signatures (XEP-0348) is to be used during registration.</param>
		/// <param name="FormSignatureSecret">Form signature secret, if form signatures (XEP-0348) is to be used during registration.</param>
		public void AllowRegistration(string FormSignatureKey, string FormSignatureSecret)
		{
			this.allowedToRegistered = true;
			this.formSignatureKey = FormSignatureKey;
			this.formSignatureSecret = FormSignatureSecret;
		}

		/// <summary>
		/// Sends an IQ Get request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqGet(string To, string Xml, IqResultEventHandler Callback, object State)
		{
			return this.SendIq(null, To, Xml, "get", Callback, State, this.defaultRetryTimeout, this.defaultNrRetries, this.defaultDropOff,
				this.defaultMaxRetryTimeout);
		}

		/// <summary>
		/// Sends an IQ Get request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <param name="RetryTimeout">Retry Timeout, in milliseconds.</param>
		/// <param name="NrRetries">Number of retries.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqGet(string To, string Xml, IqResultEventHandler Callback, object State, int RetryTimeout, int NrRetries)
		{
			return this.SendIq(null, To, Xml, "get", Callback, State, RetryTimeout, NrRetries, false, RetryTimeout);
		}

		/// <summary>
		/// Sends an IQ Get request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <param name="RetryTimeout">Retry Timeout, in milliseconds.</param>
		/// <param name="NrRetries">Number of retries.</param>
		/// <param name="DropOff">If the retry timeout should be doubled between retries (true), or if the same retry timeout 
		/// should be used for all retries. The retry timeout will never exceed <paramref name="MaxRetryTieout"/>.</param>
		/// <param name="MaxRetryTimeout">Maximum retry timeout. Used if <see cref="DropOff"/> is true.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqGet(string To, string Xml, IqResultEventHandler Callback, object State,
			int RetryTimeout, int NrRetries, bool DropOff, int MaxRetryTimeout)
		{
			return this.SendIq(null, To, Xml, "get", Callback, State, RetryTimeout, NrRetries, DropOff, MaxRetryTimeout);
		}

		/// <summary>
		/// Sends an IQ Set request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqSet(string To, string Xml, IqResultEventHandler Callback, object State)
		{
			return this.SendIq(null, To, Xml, "set", Callback, State, this.defaultRetryTimeout, this.defaultNrRetries, this.defaultDropOff,
				this.defaultMaxRetryTimeout);
		}

		/// <summary>
		/// Sends an IQ Set request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <param name="RetryTimeout">Retry Timeout, in milliseconds.</param>
		/// <param name="NrRetries">Number of retries.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqSet(string To, string Xml, IqResultEventHandler Callback, object State, int RetryTimeout, int NrRetries)
		{
			return this.SendIq(null, To, Xml, "set", Callback, State, RetryTimeout, NrRetries, false, RetryTimeout);
		}

		/// <summary>
		/// Sends an IQ Set request.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Callback">Callback method to call when response is returned.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <param name="RetryTimeout">Retry Timeout, in milliseconds.</param>
		/// <param name="NrRetries">Number of retries.</param>
		/// <param name="DropOff">If the retry timeout should be doubled between retries (true), or if the same retry timeout 
		/// should be used for all retries. The retry timeout will never exceed <paramref name="MaxRetryTieout"/>.</param>
		/// <param name="MaxRetryTimeout">Maximum retry timeout. Used if <see cref="DropOff"/> is true.</param>
		/// <returns>ID of IQ stanza</returns>
		public uint SendIqSet(string To, string Xml, IqResultEventHandler Callback, object State,
			int RetryTimeout, int NrRetries, bool DropOff, int MaxRetryTimeout)
		{
			return this.SendIq(null, To, Xml, "set", Callback, State, RetryTimeout, NrRetries, DropOff, MaxRetryTimeout);
		}

		/// <summary>
		/// Returns a response to an IQ Get/Set request.
		/// </summary>
		/// <param name="Id">ID attribute of original IQ request.</param>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the response.</param>
		public void SendIqResult(string Id, string To, string Xml)
		{
			this.SendIq(Id, To, Xml, "result", null, null, 0, 0, false, 0);
		}

		/// <summary>
		/// Returns an error response to an IQ Get/Set request.
		/// </summary>
		/// <param name="Id">ID attribute of original IQ request.</param>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the response.</param>
		public void SendIqError(string Id, string To, string Xml)
		{
			this.SendIq(Id, To, Xml, "error", null, null, 0, 0, false, 0);
		}

		private uint SendIq(string Id, string To, string Xml, string Type, IqResultEventHandler Callback, object State,
			int RetryTimeout, int NrRetries, bool DropOff, int MaxRetryTimeout)
		{
			PendingRequest PendingRequest = null;
			DateTime TP;
			uint SeqNr;

			if (string.IsNullOrEmpty(Id))
			{
				lock (this.synchObject)
				{
					SeqNr = this.seqnr++;
					PendingRequest = new PendingRequest(SeqNr, Callback, State, RetryTimeout, NrRetries, DropOff, MaxRetryTimeout, To);
					TP = PendingRequest.Timeout;

					while (this.pendingRequestsByTimeout.ContainsKey(TP))
						TP = TP.AddTicks(this.gen.Next(1, 10));

					PendingRequest.Timeout = TP;

					this.pendingRequestsBySeqNr[SeqNr] = PendingRequest;
					this.pendingRequestsByTimeout[TP] = PendingRequest;

					Id = SeqNr.ToString();
				}
			}
			else
				SeqNr = 0;

			StringBuilder XmlOutput = new StringBuilder();

			XmlOutput.Append("<iq type='");
			XmlOutput.Append(Type);
			XmlOutput.Append("' id='");
			XmlOutput.Append(Id);

			if (!string.IsNullOrEmpty(To))
			{
				XmlOutput.Append("' to='");
				XmlOutput.Append(XML.Encode(To));
			}

			XmlOutput.Append("'>");
			XmlOutput.Append(Xml);
			XmlOutput.Append("</iq>");

			string IqXml = XmlOutput.ToString();
			if (PendingRequest != null)
				PendingRequest.Xml = IqXml;

			this.BeginWrite(IqXml, null);

			return SeqNr;
		}

		/// <summary>
		/// Performs a synchronous IQ Get request/response operation.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <returns>Response XML element.</returns>
		/// <exception cref="TimeoutException">If a timeout occurred.</exception>
		/// <exception cref="XmppException">If an IQ error is returned.</exception>
		public XmlElement IqGet(string To, string Xml, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			IqResultEventArgs e = null;

			try
			{
				this.SendIqGet(To, Xml, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e.Response;
		}

		/// <summary>
		/// Performs a synchronous IQ Set request/response operation.
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Xml">XML to embed into the request.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <returns>Response XML element.</returns>
		/// <exception cref="TimeoutException">If a timeout occurred.</exception>
		/// <exception cref="XmppException">If an IQ error is returned.</exception>
		public XmlElement IqSet(string To, string Xml, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			IqResultEventArgs e = null;

			try
			{
				this.SendIqSet(To, Xml, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e.Response;
		}

		private void RegistrationFormReceived(object Sender, IqResultEventArgs e)
		{
			if (e.Ok)
			{
				foreach (XmlNode N in e.Response.ChildNodes)
				{
					if (N.LocalName == "query" && N.NamespaceURI == NamespaceRegister)
					{
						DataForm Form = null;
						string UserName = null;
						string Password = null;

						foreach (XmlNode N2 in N.ChildNodes)
						{
							switch (N2.LocalName)
							{
								case "username":
									UserName = N2.InnerText;
									break;

								case "password":
									Password = N2.InnerText;
									break;

								case "x":
									Form = new DataForm(this, (XmlElement)N2, this.SubmitRegistrationForm, this.CancelRegistrationForm, e.From, e.To);
									Form.State = e;

									Field Field = Form["username"];
									if (Field != null)
										Field.SetValue(this.userName);

									Field = Form["password"];
									if (Field != null)
										Field.SetValue(this.password);
									break;
							}
						}

						if (Form != null)
						{
							this.Information("OnRegistrationForm()");
							DataFormEventHandler h = this.OnRegistrationForm;
							if (h != null)
							{
								try
								{
									h(this, Form);
								}
								catch (Exception ex)
								{
									Exception(ex);
								}
							}
							else
								Form.Submit();
						}
						else
						{
							StringBuilder Xml = new StringBuilder();

							Xml.Append("<query xmlns='" + NamespaceRegister + "'>");

							if (UserName != null)
							{
								Xml.Append("<username>");
								Xml.Append(XML.Encode(this.userName));
								Xml.Append("</username>");
							}

							if (Password != null)
							{
								Xml.Append("<password>");
								Xml.Append(XML.Encode(this.userName));
								Xml.Append("</password>");
							}

							this.SendIqSet(e.From, Xml.ToString(), this.RegistrationResultReceived, null);
						}
						return;
					}
				}
			}

			this.ConnectionError(e.StanzaError != null ? e.StanzaError : new XmppException("Unable to register new account.", e.Response));
		}

		/// <summary>
		/// Event raised when a registration form is shown during automatic account creation during connection.
		/// </summary>
		public event DataFormEventHandler OnRegistrationForm = null;

		private void SubmitRegistrationForm(object Sender, DataForm RegistrationForm)
		{
			IqResultEventArgs e = (IqResultEventArgs)RegistrationForm.State;
			StringBuilder Xml = new StringBuilder();

			if (!string.IsNullOrEmpty(this.formSignatureKey) && !string.IsNullOrEmpty(this.formSignatureSecret))
				RegistrationForm.Sign(this.formSignatureKey, this.formSignatureSecret);

			Xml.Append("<query xmlns='" + NamespaceRegister + "'>");
			RegistrationForm.SerializeSubmit(Xml);
			Xml.Append("</query>");

			this.SendIqSet(e.From, Xml.ToString(), this.RegistrationResultReceived, null);
		}

		private void CancelRegistrationForm(object Sender, DataForm RegistrationForm)
		{
			IqResultEventArgs e = (IqResultEventArgs)RegistrationForm.State;
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='" + NamespaceRegister + "'>");
			RegistrationForm.SerializeCancel(Xml);
			Xml.Append("</query>");

			this.SendIqSet(e.From, Xml.ToString(), null, null);
		}

		private void RegistrationResultReceived(object Sender, IqResultEventArgs e)
		{
			if (e.Ok)
			{
				this.authenticationMethod = null;
				this.StartAuthentication();
			}
			else
				this.ConnectionError(e.StanzaError != null ? e.StanzaError : new XmppException("Unable to register new account.", e.Response));
		}

		private void BindResult(object Sender, IqResultEventArgs e)
		{
			if (e.Ok)
			{
				foreach (XmlNode N in e.Response.ChildNodes)
				{
					if (N.LocalName == "bind")
					{
						foreach (XmlNode N2 in N.ChildNodes)
						{
							if (N2.LocalName == "jid")
							{
								this.fullJid = N2.InnerText.Trim();

								int i = this.fullJid.IndexOf('/');
								if (i > 0)
								{
									this.resource = this.fullJid.Substring(i + 1);

									i = this.resource.IndexOf(' ');
									if (i > 0)
									{
										this.resource = this.resource.Substring(i + 1).TrimStart();
										this.fullJid = this.bareJid + "/" + this.resource;
									}
								}

								this.AdvanceUntilConnected();
								return;
							}
						}
					}
				}
			}

			this.ConnectionError(e.StanzaError != null ? e.StanzaError : new XmppException("Unable to bind the connection.", e.Response));
		}

		/// <summary>
		/// Changes the password of the current user.
		/// </summary>
		/// <param name="NewPassword">New password.</param>
		public void ChangePassword(string NewPassword)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='" + NamespaceRegister + "'><username>");
			Xml.Append(XML.Encode(this.userName));
			Xml.Append("</username><password>");
			Xml.Append(XML.Encode(NewPassword));
			Xml.Append("</password></query>");

			this.SendIqSet(this.domain, Xml.ToString(), this.ChangePasswordResult, new object[] { NewPassword, true });
		}

		private void ChangePasswordResult(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			string NewPassword = (string)P[0];
			bool FirstAttempt = (bool)P[1];

			if (e.Ok)
			{
				this.password = NewPassword;

				// TODO: Also update hash and hash method

				this.Information("OnPasswordChanged()");
				EventHandler h = this.OnPasswordChanged;
				if (h != null)
				{
					try
					{
						h(this, new EventArgs());
					}
					catch (Exception ex)
					{
						Exception(ex);
					}
				}
			}
			else
			{
				if (e.ErrorType == ErrorType.Modify)
				{
					foreach (XmlNode N in e.Response.ChildNodes)
					{
						if (N.LocalName == "query" && N.NamespaceURI == NamespaceRegister)
						{
							foreach (XmlNode N2 in N.ChildNodes)
							{
								if (N2.LocalName == "x" && N2.NamespaceURI == NamespaceData)
								{
									DataForm Form = new DataForm(this, (XmlElement)N2, this.SubmitChangePasswordForm, this.CancelChangePasswordForm, e.From, e.To);
									Form.State = e;

									Field Field = Form["username"];
									if (Field != null)
										Field.SetValue(this.userName);

									Field = Form["old_password"];
									if (Field != null)
										Field.SetValue(this.password);

									Field = Form["password"];
									if (Field != null)
										Field.SetValue(NewPassword);

									this.Information("OnChangePasswordForm()");
									DataFormEventHandler h = this.OnChangePasswordForm;
									if (h != null)
									{
										try
										{
											h(this, Form);
										}
										catch (Exception ex)
										{
											Exception(ex);
										}

										return;
									}
									else if (FirstAttempt)
									{
										Form.Submit();
										return;
									}
								}
							}
						}
					}
				}

				this.Error(e.StanzaError);
			}
		}

		private void SubmitChangePasswordForm(object Sender, DataForm RegistrationForm)
		{
			IqResultEventArgs e = (IqResultEventArgs)RegistrationForm.State;
			StringBuilder Xml = new StringBuilder();

			if (!string.IsNullOrEmpty(this.formSignatureKey) && !string.IsNullOrEmpty(this.formSignatureSecret))
				RegistrationForm.Sign(this.formSignatureKey, this.formSignatureSecret);

			Xml.Append("<query xmlns='" + NamespaceRegister + "'>");
			RegistrationForm.SerializeSubmit(Xml);
			Xml.Append("</query>");

			this.SendIqSet(e.From, Xml.ToString(), this.ChangePasswordResult, e.State);
		}

		private void CancelChangePasswordForm(object Sender, DataForm RegistrationForm)
		{
			IqResultEventArgs e = (IqResultEventArgs)RegistrationForm.State;
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='" + NamespaceRegister + "'>");
			RegistrationForm.SerializeCancel(Xml);
			Xml.Append("</query>");

			this.SendIqSet(e.From, Xml.ToString(), null, null);
		}

		/// <summary>
		/// Event raised when a change password form is shown during password change.
		/// </summary>
		public event DataFormEventHandler OnChangePasswordForm = null;

		/// <summary>
		/// Event raised when password has been changed.
		/// </summary>
		public event EventHandler OnPasswordChanged = null;

		/// <summary>
		/// If the roster should be automatically fetched on startup or not.
		/// </summary>
		public bool RequestRosterOnStartup
		{
			get { return this.requestRosterOnStartup; }
			set { this.requestRosterOnStartup = value; }
		}

		private void AdvanceUntilConnected()
		{
			if (!this.hasRoster && this.requestRosterOnStartup)
			{
				this.State = XmppState.FetchingRoster;
				this.SendIqGet(this.domain, "<query xmlns='" + NamespaceRoster + "'/>", this.RosterResult, null);
			}
			else if (!this.setPresence)
			{
				this.State = XmppState.SettingPresence;
				this.SetPresence(this.currentAvailability, this.customPresenceXml, this.customPresenceStatus);
			}
			else
			{
				this.State = XmppState.Connected;

				this.secondTimer = new Timer(this.SecondTimerCallback, null, 1000, 1000);
			}
		}

		private void RosterResult(object Client, IqResultEventArgs e)
		{
			RosterItem Item;

			if (e.Ok)
			{
				this.hasRoster = true;

				foreach (XmlNode N in e.Response.ChildNodes)
				{
					if (N.LocalName == "query" && N.NamespaceURI == NamespaceRoster)
					{
						lock (this.roster)
						{
							this.roster.Clear();

							foreach (XmlNode N2 in N.ChildNodes)
							{
								if (N2.LocalName == "item")
								{
									Item = new RosterItem((XmlElement)N2);
									this.roster[Item.BareJid] = Item;
								}
							}
						}
					}
				}

				this.AdvanceUntilConnected();
			}
			else
				this.ConnectionError(e.StanzaError != null ? e.StanzaError : new XmppException("Unable to fetch roster.", e.Response));
		}

		/// <summary>
		/// Access to the roster in the client.
		/// 
		/// To add or update a roster item, simply set the corresponding property value. To remove a roster item, set it to null.
		/// </summary>
		/// <param name="BareJID">Bare JID of roster item.</param>
		/// <returns>Roster item, if found, or null, if not available.</returns>
		/// <exception cref="ArgumentException">If updating a roster item with an item that doesn't have the same bare JID as <paramref name="BareJID"/>.</exception>
		public RosterItem this[string BareJID]
		{
			get
			{
				return this.GetRosterItem(BareJID);
			}

			set
			{
				if (value == null)
					this.RemoveRosterItem(BareJID, null, null);
				else if (BareJID != value.BareJid)
					throw new ArgumentException("Bare JIDs don't match.", "BareJID");
				else
					this.AddRosterItem(value, null, null);
			}
		}

		/// <summary>
		/// Gets a roster item.
		/// </summary>
		/// <param name="BareJID">Bare JID of roster item.</param>
		/// <returns>Roster item, if found, or null, if not available.</returns>
		public RosterItem GetRosterItem(string BareJID)
		{
			RosterItem RosterItem;

			lock (this.roster)
			{
				if (this.roster.TryGetValue(BareJID, out RosterItem))
					return RosterItem;
				else
					return null;
			}
		}

		/// <summary>
		/// Adds an item to the roster. If an item with the same Bare JID is found in the roster, that item is updated.
		/// </summary>
		/// <param name="Item">Item to add.</param>
		public void AddRosterItem(RosterItem Item)
		{
			this.AddRosterItem(Item, null, null);
		}

		/// <summary>
		/// Adds an item to the roster. If an item with the same Bare JID is found in the roster, that item is updated.
		/// </summary>
		/// <param name="Item">Item to add.</param>
		/// <param name="Callback">Callback method to call, when roster has been updated. Can be null.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		public void AddRosterItem(RosterItem Item, IqResultEventHandler Callback, object State)
		{
			RosterItem RosterItem;

			lock (this.roster)
			{
				if (this.roster.TryGetValue(BareJID, out RosterItem))
				{
					Item.PendingSubscription = RosterItem.PendingSubscription;
					Item.State = RosterItem.State;
				}

				this.roster[BareJID] = Item;
			}

			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceRoster);
			Xml.Append("'>");

			Item.Serialize(Xml);

			Xml.Append("</query>");

			this.SendIqSet(this.domain, Xml.ToString(), Callback, State);
		}

		/// <summary>
		/// Updates an item in the roster.
		/// </summary>
		/// <param name="BareJID">Bare JID of the roster item.</param>
		/// <param name="Name">New name for the item.</param>
		/// <param name="Groups">Set of groups assigned to the item.</param>
		/// <exception cref="ArgumentException">If there is no roste item available with the corresponding bare JID.</exception>
		public void UpdateRosterItem(string BareJID, string Name, params string[] Groups)
		{
			this.UpdateRosterItem(BareJID, Name, Groups, null, null);
		}

		/// <summary>
		/// Updates an item in the roster.
		/// </summary>
		/// <param name="BareJID">Bare JID of the roster item.</param>
		/// <param name="Name">New name for the item.</param>
		/// <param name="Groups">Set of groups assigned to the item.</param>
		/// <param name="Callback">Callback method to call, when roster has been updated. Can be null.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <exception cref="ArgumentException">If there is no roster item available with the corresponding bare JID.</exception>
		public void UpdateRosterItem(string BareJID, string Name, string[] Groups, IqResultEventHandler Callback, object State)
		{
			RosterItem RosterItem;

			lock (this.roster)
			{
				if (!this.roster.TryGetValue(BareJID, out RosterItem))
					throw new ArgumentException("A Roster Item with that bare JID was not found.", "BareJID");

				RosterItem.Name = Name;
				RosterItem.Groups = Groups;
			}

			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceRoster);
			Xml.Append("'>");

			RosterItem.Serialize(Xml);

			Xml.Append("</query>");

			this.SendIqSet(this.domain, Xml.ToString(), Callback, State);
		}

		/// <summary>
		/// Removes an item from the roster.
		/// </summary>
		/// <param name="BareJID">Bare JID of the roster item.</param>
		/// <exception cref="ArgumentException">If there is no roster item available with the corresponding bare JID.</exception>
		public void RemoveRosterItem(string BareJID)
		{
			this.RemoveRosterItem(BareJID, null, null);
		}

		/// <summary>
		/// Removes an item from the roster.
		/// </summary>
		/// <param name="BareJID">Bare JID of the roster item.</param>
		/// <param name="Callback">Callback method to call, when roster has been updated. Can be null.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		/// <exception cref="ArgumentException">If there is no roster item available with the corresponding bare JID.</exception>
		public void RemoveRosterItem(string BareJID, IqResultEventHandler Callback, object State)
		{
			lock (this.roster)
			{
				if (!this.roster.Remove(BareJID))
					throw new ArgumentException("A Roster Item with that bare JID was not found.", "BareJID");
			}

			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceRoster);
			Xml.Append("'><item jid='");
			Xml.Append(XML.Encode(BareJID));
			Xml.Append("' subscription='remove'/></query>");

			this.SendIqSet(this.domain, Xml.ToString(), Callback, State);
		}

		/// <summary>
		/// If the roster has been fetched.
		/// </summary>
		public bool HasRoster
		{
			get { return this.hasRoster; }
		}

		/// <summary>
		/// Items in the roster.
		/// </summary>
		public RosterItem[] Roster
		{
			get
			{
				RosterItem[] Result;

				lock (this.roster)
				{
					Result = new RosterItem[this.roster.Count];
					this.roster.Values.CopyTo(Result, 0);
				}

				return Result;
			}
		}

		/// <summary>
		/// Sets the presence of the connection.
		/// </summary>
		public void SetPresence()
		{
			this.SetPresence(Availability.Online, string.Empty, null);
		}

		/// <summary>
		/// Sets the presence of the connection.
		/// </summary>
		/// <param name="Availability">Client availability.</param>
		public void SetPresence(Availability Availability)
		{
			this.SetPresence(Availability, string.Empty, null);
		}

		/// <summary>
		/// Sets the presence of the connection.
		/// </summary>
		/// <param name="Availability">Client availability.</param>
		/// <param name="CustomXml">Custom XML.</param>
		public void SetPresence(Availability Availability, string CustomXml)
		{
			this.SetPresence(Availability, CustomXml, null);
		}

		/// <summary>
		/// Sets the presence of the connection.
		/// </summary>
		/// <param name="Availability">Client availability.</param>
		/// <param name="CustomXml">Custom XML.</param>
		/// <param name="Status">Custom Status message, defined as a set of (language,text) pairs.</param>
		public void SetPresence(Availability Availability, string CustomXml, params KeyValuePair<string, string>[] Status)
		{
			this.currentAvailability = Availability;
			this.customPresenceXml = CustomXml;
			this.customPresenceStatus = Status;

			if (this.state == XmppState.Connected || this.state == XmppState.SettingPresence)
			{
				StringBuilder Xml = new StringBuilder();

				switch (Availability)
				{
					case XMPP.Availability.Online:
					default:
						Xml.Append("<presence>");
						break;

					case XMPP.Availability.Away:
						Xml.Append("<presence><show>away</show>");
						break;

					case XMPP.Availability.Chat:
						Xml.Append("<presence><show>chat</show>");
						break;

					case XMPP.Availability.DoNotDisturb:
						Xml.Append("<presence><show>dnd</show>");
						break;

					case XMPP.Availability.ExtendedAway:
						Xml.Append("<presence><show>xa</show>");
						break;

					case XMPP.Availability.Offline:
						Xml.Append("<presence type='unavailable'>");
						break;
				}

				if (Status != null)
				{
					foreach (KeyValuePair<string, string> P in Status)
					{
						Xml.Append("<status");

						if (!string.IsNullOrEmpty(P.Key))
						{
							Xml.Append(" xml:lang='");
							Xml.Append(XML.Encode(P.Key));
							Xml.Append("'>");
						}
						else
							Xml.Append('>');

						Xml.Append(XML.Encode(P.Value));
						Xml.Append("</status>");
					}
				}

				if (!string.IsNullOrEmpty(CustomXml))
					Xml.Append(CustomXml);

				Xml.Append("</presence>");

				this.BeginWrite(Xml.ToString(), this.PresenceSent);
			}
		}

		private void PresenceSent(object Sender, EventArgs e)
		{
			if (!this.setPresence)
			{
				this.setPresence = true;
				this.AdvanceUntilConnected();
			}
		}

		/// <summary>
		/// Requests subscription of presence information from a contact.
		/// </summary>
		/// <param name="BareJid">Bare JID of contact.</param>
		public void RequestPresenceSubscription(string BareJid)
		{
			StringBuilder Xml = new StringBuilder();
			uint SeqNr;

			lock (this.synchObject)
			{
				SeqNr = this.seqnr++;
			}

			Xml.Append("<presence id='");
			Xml.Append(SeqNr.ToString());
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='subscribe'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		/// <summary>
		/// Requests unssubscription of presence information from a contact.
		/// </summary>
		/// <param name="BareJid">Bare JID of contact.</param>
		public void RequestPresenceUnsubscription(string BareJid)
		{
			StringBuilder Xml = new StringBuilder();
			uint SeqNr;

			lock (this.synchObject)
			{
				SeqNr = this.seqnr++;
			}

			Xml.Append("<presence id='");
			Xml.Append(SeqNr.ToString());
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='unsubscribe'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		internal void PresenceSubscriptionAccepted(string Id, string BareJid)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<presence id='");
			Xml.Append(XML.Encode(Id));
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='subscribed'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		internal void PresenceSubscriptionDeclined(string Id, string BareJid)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<presence id='");
			Xml.Append(XML.Encode(Id));
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='unsubscribed'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		internal void PresenceUnsubscriptionAccepted(string Id, string BareJid)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<presence id='");
			Xml.Append(XML.Encode(Id));
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='unsubscribed'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		internal void PresenceUnsubscriptionDeclined(string Id, string BareJid)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<presence id='");
			Xml.Append(XML.Encode(Id));
			Xml.Append("' to='");
			Xml.Append(XML.Encode(BareJid));
			Xml.Append("' type='subscribed'/>");

			this.BeginWrite(Xml.ToString(), null);
		}

		private void RosterPushHandler(object Sender, IqEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.From))
				return;

			RosterItem Prev = null;
			RosterItem Item = null;

			foreach (XmlElement E in e.Query.ChildNodes)
			{
				if (E.LocalName == "item" && E.NamespaceURI == NamespaceRoster)
				{
					Item = new RosterItem(E);
					break;
				}
			}

			if (Item == null)
				throw new BadRequestException(string.Empty, e.Query);

			RosterItemEventHandler h;

			this.SendIqResult(e.Id, e.From, string.Empty);

			lock (this.roster)
			{
				if (Item.State == SubscriptionState.Remove)
				{
					this.roster.Remove(Item.BareJid);
					this.Information("OnRosterItemRemoved()");
					h = this.OnRosterItemRemoved;
				}
				else
				{
					if (this.roster.TryGetValue(Item.BareJid, out Prev))
					{
						this.Information("OnRosterItemUpdated()");
						h = this.OnRosterItemUpdated;
						if (Prev.HasLastPresence)
							Item.LastPresence = Prev.LastPresence;
					}
					else
					{
						this.Information("OnRosterItemAdded()");
						h = this.OnRosterItemAdded;
						this.roster[Item.BareJid] = Item;
					}
				}
			}

			if (h != null)
			{
				try
				{
					h(this, Item);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		/// <summary>
		/// Event raised when an item has been added to the roster.
		/// </summary>
		public event RosterItemEventHandler OnRosterItemAdded = null;

		/// <summary>
		/// Event raised when an item has been updated in the roster.
		/// </summary>
		public event RosterItemEventHandler OnRosterItemUpdated = null;

		/// <summary>
		/// Event raised when an item has been removed from the roster.
		/// </summary>
		public event RosterItemEventHandler OnRosterItemRemoved = null;

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Body">Body text of chat message.</param>
		public void SendChatMessage(string To, string Body)
		{
			this.SendMessage(QoSLevel.Unacknowledged, MessageType.Chat, To, string.Empty, Body, string.Empty, string.Empty, string.Empty, string.Empty, 
				null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		public void SendChatMessage(string To, string Body, string Subject)
		{
			this.SendMessage(QoSLevel.Unacknowledged, MessageType.Chat, To, string.Empty, Body, Subject, string.Empty, string.Empty, string.Empty, null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		/// <param name="Language">Language used.</param>
		public void SendChatMessage(string To, string Body, string Subject, string Language)
		{
			this.SendMessage(QoSLevel.Unacknowledged, MessageType.Chat, To, string.Empty, Body, Subject, Language, string.Empty, string.Empty, null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		/// <param name="Language">Language used.</param>
		/// <param name="ThreadId">Thread ID</param>
		public void SendChatMessage(string To, string Body, string Subject, string Language, string ThreadId)
		{
			this.SendMessage(QoSLevel.Unacknowledged, MessageType.Chat, To, string.Empty, Body, Subject, Language, ThreadId, string.Empty, null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="To">Destination address</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		/// <param name="Language">Language used.</param>
		/// <param name="ThreadId">Thread ID</param>
		/// <param name="ParentThreadId">Parent Thread ID</param>
		public void SendChatMessage(string To, string Body, string Subject, string Language, string ThreadId, string ParentThreadId)
		{
			this.SendMessage(QoSLevel.Unacknowledged, MessageType.Chat, To, string.Empty, Body, Subject, Language, ThreadId, ParentThreadId, null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="Type">Type of message to send.</param>
		/// <param name="To">Destination address</param>
		/// <param name="CustomXml">Custom XML</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		/// <param name="Language">Language used.</param>
		/// <param name="ThreadId">Thread ID</param>
		/// <param name="ParentThreadId">Parent Thread ID</param>
		public void SendMessage(MessageType Type, string To, string CustomXml, string Body, string Subject, string Language, string ThreadId,
			string ParentThreadId)
		{
			this.SendMessage(QoSLevel.Unacknowledged, Type, To, CustomXml, Body, Subject, Language, ThreadId, ParentThreadId, null, null);
		}

		/// <summary>
		/// Sends a simple chat message
		/// </summary>
		/// <param name="QoS">Quality of Service level of message.</param>
		/// <param name="Type">Type of message to send.</param>
		/// <param name="To">Destination address</param>
		/// <param name="CustomXml">Custom XML</param>
		/// <param name="Body">Body text of chat message.</param>
		/// <param name="Subject">Subject</param>
		/// <param name="Language">Language used.</param>
		/// <param name="ThreadId">Thread ID</param>
		/// <param name="ParentThreadId">Parent Thread ID</param>
		/// <param name="DeliveryCallback">Callback to call when message has been sent, or failed to be sent.</param>
		/// <param name="State">State object to pass on to the callback method.</param>
		public void SendMessage(QoSLevel QoS, MessageType Type, string To, string CustomXml, string Body, string Subject, string Language, string ThreadId,
			string ParentThreadId, DeliveryEventHandler DeliveryCallback, object State)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<message");

			switch (Type)
			{
				case MessageType.Chat:
					Xml.Append(" type='chat'");
					break;

				case MessageType.Error:
					Xml.Append(" type='error'");
					break;

				case MessageType.GroupChat:
					Xml.Append(" type='groupchat'");
					break;

				case MessageType.Headline:
					Xml.Append(" type='headline'");
					break;
			}

			if (QoS == QoSLevel.Unacknowledged)
			{
				Xml.Append(" to='");
				Xml.Append(XML.Encode(To));
				Xml.Append('\'');
			}

			if (!string.IsNullOrEmpty(Language))
			{
				Xml.Append(" xml:lang='");
				Xml.Append(XML.Encode(Language));
				Xml.Append('\'');
			}

			Xml.Append('>');

			if (!string.IsNullOrEmpty(Subject))
			{
				Xml.Append("<subject>");
				Xml.Append(XML.Encode(Subject));
				Xml.Append("</subject>");
			}

			Xml.Append("<body>");
			Xml.Append(XML.Encode(Body));
			Xml.Append("</body>");

			if (!string.IsNullOrEmpty(ThreadId))
			{
				Xml.Append("<thread");

				if (!string.IsNullOrEmpty(ParentThreadId))
				{
					Xml.Append(" parent='");
					Xml.Append(XML.Encode(ParentThreadId));
					Xml.Append("'");
				}

				Xml.Append(">");
				Xml.Append(XML.Encode(ThreadId));
				Xml.Append("</thread>");
			}

			if (!string.IsNullOrEmpty(CustomXml))
				Xml.Append(CustomXml);

			Xml.Append("</message>");

			string MessageXml = Xml.ToString();

			switch (QoS)
			{
				case QoSLevel.Unacknowledged:
					this.BeginWrite(MessageXml, (sender, e) => this.DeliveryCallback(DeliveryCallback, State, true));
					break;

				case QoSLevel.Acknowledged:
					Xml.Clear();
					Xml.Append("<qos:acknowledged xmlns:qos='urn:xmpp:qos'>");
					Xml.Append(MessageXml);
					Xml.Append("</qos:acknowledged>");

					this.SendIqSet(To, Xml.ToString(), (sender, e) => this.DeliveryCallback(DeliveryCallback, State, e.Ok), null,
						2000, int.MaxValue, true, 3600000);
					break;

				case QoSLevel.Assured:
					string MsgId = Guid.NewGuid().ToString().Replace("-", string.Empty);

					Xml.Clear();
					Xml.Append("<qos:assured xmlns:qos='urn:xmpp:qos' msgId='");
					Xml.Append(MsgId);
					Xml.Append("'>");
					Xml.Append(MessageXml);
					Xml.Append("</qos:assured>");

					this.SendIqSet(To, Xml.ToString(), this.AssuredDeliveryStep, new object[] { DeliveryCallback, State, MsgId },
						2000, int.MaxValue, true, 3600000);
					break;
			}
		}

		private void AssuredDeliveryStep(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			DeliveryEventHandler DeliveryCallback = (DeliveryEventHandler)P[0];
			object State = P[1];
			string MsgId = (string)P[2];

			if (e.Ok)
			{
				foreach (XmlNode N in e.Response)
				{
					if (N.LocalName == "received")
					{
						if (MsgId == XML.Attribute((XmlElement)N, "msgId"))
						{
							StringBuilder Xml = new StringBuilder();

							Xml.Append("<qos:deliver xmlns:qos='urn:xmpp:qos' msgId='");
							Xml.Append(MsgId);
							Xml.Append("'/>");

							this.SendIqSet(e.From, Xml.ToString(), (sender, e2) => this.DeliveryCallback(DeliveryCallback, State, e2.Ok), null,
								2000, int.MaxValue, true, 3600000);
							return;
						}
					}
				}
			}

			this.DeliveryCallback(DeliveryCallback, State, false);
		}

		private void DeliveryCallback(DeliveryEventHandler Callback, object State, bool Ok)
		{
			if (Callback != null)
			{
				try
				{
					Callback(this, new DeliveryEventArgs(State, Ok));
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		private void DynamicFormUpdatedHandler(object Sender, MessageEventArgs e)
		{
			DataForm Form = null;
			string SessionVariable = XML.Attribute(e.Content, "sessionVariable");
			string Language = XML.Attribute(e.Content, "xml:lang");

			foreach (XmlNode N in e.Content.ChildNodes)
			{
				if (N.LocalName == "x")
				{
					Form = new DataForm(this, (XmlElement)N, null, null, e.From, e.To);
					break;
				}
			}

			if (Form != null)
			{
				DynamicDataFormEventHandler h = this.OnDynamicFormUpdated;
				if (h != null)
				{
					try
					{
						h(this, new DynamicDataFormEventArgs(Form, SessionVariable, Language));
					}
					catch (Exception ex)
					{
						this.Exception(ex);
					}
				}
			}
		}

		/// <summary>
		/// Event raised when a dynamic for has been updated. Dynamic forms have to be joined to the previous form 
		/// using the <see cref="DataForm.Join"/> method on the old form. The old form is identified using
		/// <see cref="DynamicDataFormEventArgs.SessionVariable"/>.
		/// </summary>
		public event DynamicDataFormEventHandler OnDynamicFormUpdated = null;

		private void ServiceDiscoveryRequestHandler(object Sender, IqEventArgs e)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceServiceDiscoveryInfo);
			Xml.Append("'><identity category='client' type='pc' name='");
			Xml.Append(XML.Encode(this.clientName));
			Xml.Append("'/>");

			lock (this.synchObject)
			{
				foreach (string Feature in this.clientFeatures.Keys)
				{
					Xml.Append("<feature var='");
					Xml.Append(XML.Encode(Feature));
					Xml.Append("'/>");
				}
			}

			Xml.Append("</query>");

			e.IqResult(Xml.ToString());
		}

		/// <summary>
		/// Sends a service discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendServiceDiscoveryRequest(string To, ServiceDiscoveryEventHandler Callback, object State)
		{
			this.SendServiceDiscoveryRequest(To, string.Empty, Callback, State);
		}

		/// <summary>
		/// Sends a service discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Node">Optional node.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendServiceDiscoveryRequest(string To, string Node, ServiceDiscoveryEventHandler Callback, object State)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceServiceDiscoveryInfo);

			if (!string.IsNullOrEmpty(Node))
			{
				Xml.Append("' node='");
				Xml.Append(XML.Encode(Node));
			}

			Xml.Append("'/>");

			this.SendIqGet(To, Xml.ToString(), this.ServiceDiscoveryResponse, new object[] { Callback, State });
		}

		private void ServiceDiscoveryResponse(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			ServiceDiscoveryEventHandler Callback = (ServiceDiscoveryEventHandler)P[0];
			object State = P[1];
			Dictionary<string, bool> Features = new Dictionary<string, bool>();
			List<Identity> Identities = new List<Identity>();

			if (Callback != null)
			{
				if (e.Ok)
				{
					foreach (XmlNode N in e.Response.ChildNodes)
					{
						if (N.LocalName == "query")
						{
							foreach (XmlNode N2 in N.ChildNodes)
							{
								switch (N2.LocalName)
								{
									case "identity":
										Identities.Add(new Identity((XmlElement)N2));
										break;

									case "feature":
										Features[XML.Attribute((XmlElement)N2, "var")] = true;
										break;
								}
							}
						}
					}
				}

				ServiceDiscoveryEventArgs e2 = new ServiceDiscoveryEventArgs(e, Features, Identities.ToArray());
				e2.State = State;

				try
				{
					Callback(this, e2);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		/// <summary>
		/// Sends a service discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		public ServiceDiscoveryEventArgs ServiceDiscovery(string To, int Timeout)
		{
			return this.ServiceDiscovery(To, string.Empty, Timeout);
		}

		/// <summary>
		/// Sends a service discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Node">Optional node.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		/// <exception cref="XmppException">If an IQ error is returned.</exception>
		public ServiceDiscoveryEventArgs ServiceDiscovery(string To, string Node, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			ServiceDiscoveryEventArgs e = null;

			try
			{
				this.SendServiceDiscoveryRequest(To, Node, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e;
		}

		/// <summary>
		/// Sends a service items discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendServiceItemsDiscoveryRequest(string To, ServiceItemsDiscoveryEventHandler Callback, object State)
		{
			this.SendServiceItemsDiscoveryRequest(To, string.Empty, Callback, State);
		}

		/// <summary>
		/// Sends a service items discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Node">Optional node.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendServiceItemsDiscoveryRequest(string To, string Node, ServiceItemsDiscoveryEventHandler Callback, object State)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceServiceDiscoveryItems);

			if (!string.IsNullOrEmpty(Node))
			{
				Xml.Append("' node='");
				Xml.Append(XML.Encode(Node));
			}

			Xml.Append("'/>");

			this.SendIqGet(To, Xml.ToString(), this.ServiceItemsDiscoveryResponse, new object[] { Callback, State });
		}

		private void ServiceItemsDiscoveryResponse(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			ServiceItemsDiscoveryEventHandler Callback = (ServiceItemsDiscoveryEventHandler)P[0];
			object State = P[1];
			List<Item> Items = new List<Item>();

			if (Callback != null)
			{
				if (e.Ok)
				{
					foreach (XmlNode N in e.Response.ChildNodes)
					{
						if (N.LocalName == "query")
						{
							foreach (XmlNode N2 in N.ChildNodes)
							{
								if (N2.LocalName == "item")
									Items.Add(new Item((XmlElement)N2));
							}
						}
					}
				}

				ServiceItemsDiscoveryEventArgs e2 = new ServiceItemsDiscoveryEventArgs(e, Items.ToArray());
				e2.State = State;

				try
				{
					Callback(this, e2);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
				}
			}
		}

		/// <summary>
		/// Sends a service items discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		public ServiceItemsDiscoveryEventArgs ServiceItemsDiscovery(string To, int Timeout)
		{
			return this.ServiceItemsDiscovery(To, string.Empty, Timeout);
		}

		/// <summary>
		/// Sends a service items discovery request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Node">Optional node.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		/// <exception cref="XmppException">If an IQ error is returned.</exception>
		public ServiceItemsDiscoveryEventArgs ServiceItemsDiscovery(string To, string Node, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			ServiceItemsDiscoveryEventArgs e = null;

			try
			{
				this.SendServiceItemsDiscoveryRequest(To, Node, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e;
		}

		private void SoftwareVersionRequestHandler(object Sender, IqEventArgs e)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceSoftwareVersion);
			Xml.Append("'><name>");
			Xml.Append(XML.Encode(this.clientName));
			Xml.Append("</name><version>");
			Xml.Append(XML.Encode(this.clientVersion));
			Xml.Append("</version><os>");
			Xml.Append(XML.Encode(this.clientOS));
			Xml.Append("</os></query>");

			e.IqResult(Xml.ToString());
		}

		/// <summary>
		/// Sends a software version request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendSoftwareVersionRequest(string To, SoftwareVersionEventHandler Callback, object State)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceSoftwareVersion);
			Xml.Append("'/>");

			this.SendIqGet(To, Xml.ToString(), this.SoftwareVersionResponse, new object[] { Callback, State });
		}

		private void SoftwareVersionResponse(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			SoftwareVersionEventHandler Callback = (SoftwareVersionEventHandler)P[0];
			object State = P[1];
			List<Item> Items = new List<Item>();

			if (Callback != null)
			{
				if (e.Ok)
				{
					foreach (XmlNode N in e.Response.ChildNodes)
					{
						if (N.LocalName == "query")
						{
							string Name = string.Empty;
							string Version = string.Empty;
							string OS = string.Empty;

							foreach (XmlNode N2 in N.ChildNodes)
							{
								switch (N2.LocalName)
								{
									case "name":
										Name = N2.InnerText;
										break;

									case "version":
										Version = N2.InnerText;
										break;

									case "os":
										OS = N2.InnerText;
										break;
								}
							}

							SoftwareVersionEventArgs e2 = new SoftwareVersionEventArgs(e, Name, Version, OS);
							e2.State = State;

							try
							{
								Callback(this, e2);
							}
							catch (Exception ex)
							{
								this.Exception(ex);
							}

							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Sends a software version request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		/// <returns>Version information.</returns>
		public SoftwareVersionEventArgs SoftwareVersion(string To, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			SoftwareVersionEventArgs e = null;

			try
			{
				this.SendSoftwareVersionRequest(To, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e;
		}

		/// <summary>
		/// Sends a search form request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Callback">Method to call when response or error is returned.</param>
		/// <param name="State">State object to pass on to callback method.</param>
		public void SendSearchFormRequest(string To, SearchFormEventHandler Callback, object State)
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<query xmlns='");
			Xml.Append(NamespaceSearch);
			Xml.Append("'/>");

			this.SendIqGet(To, Xml.ToString(), this.SearchFormResponse, new object[] { Callback, State });
		}

		private void SearchFormResponse(object Sender, IqResultEventArgs e)
		{
			object[] P = (object[])e.State;
			SearchFormEventHandler Callback = (SearchFormEventHandler)P[0];
			object State = P[1];
			List<Item> Items = new List<Item>();

			if (Callback != null)
			{
				if (e.Ok)
				{
					foreach (XmlNode N in e.Response.ChildNodes)
					{
						if (N.LocalName == "query")
						{
							DataForm SearchForm = null;
							string Instructions = null;
							string First = null;
							string Last = null;
							string Nick = null;
							string EMail = null;

							foreach (XmlNode N2 in N.ChildNodes)
							{
								switch (N2.LocalName)
								{
									case "instructions":
										Instructions = N2.InnerText;
										break;

									case "first":
										First = N2.InnerText;
										break;

									case "last":
										Last = N2.InnerText;
										break;

									case "nick":
										Nick = N2.InnerText;
										break;

									case "email":
										EMail = N2.InnerText;
										break;

									case "x":
										SearchForm = new DataForm(this, (XmlElement)N2, null, null, e.From, e.To);
										break;

								}
							}

							SearchFormEventArgs e2 = new SearchFormEventArgs(this, e, Instructions, First, Last, Nick, EMail, SearchForm);
							e2.State = State;
							if (SearchForm != null)
								SearchForm.State = e2;

							try
							{
								Callback(this, e2);
							}
							catch (Exception ex)
							{
								this.Exception(ex);
							}

							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Performs a search form request
		/// </summary>
		/// <param name="To">Destination address.</param>
		/// <param name="Timeout">Timeout in milliseconds.</param>
		/// <exception cref="TimeoutException">If timeout occurs.</exception>
		public SearchFormEventArgs SearchForm(string To, int Timeout)
		{
			ManualResetEvent Done = new ManualResetEvent(false);
			SearchFormEventArgs e = null;

			try
			{
				this.SendSearchFormRequest(To, (sender, e2) =>
				{
					e = e2;
					Done.Set();
				}, null);

				if (!Done.WaitOne(Timeout))
					throw new TimeoutException();
			}
			finally
			{
				Done.Close();
			}

			if (!e.Ok)
				throw e.StanzaError;

			return e;
		}

		internal static string Concat(params string[] Rows)
		{
			if (Rows == null)
				return string.Empty;

			StringBuilder sb = null;

			foreach (string s in Rows)
			{
				if (sb == null)
					sb = new StringBuilder(s);
				else
				{
					sb.AppendLine();
					sb.Append(s);
				}
			}

			if (sb == null)
				return string.Empty;
			else
				return sb.ToString();
		}

		/// <summary>
		/// Number of seconds before a network connection risks being closed by the network, if no communication is done over it.
		/// To avoid this, ping messages are sent over the network with an interval of half this value (in seconds).
		/// </summary>
		public int KeepAliveSeconds
		{
			get { return this.keepAliveSeconds; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "KeepAliveSeconds");

				this.keepAliveSeconds = value;
			}
		}

		private void AcknowledgedQoSMessageHandler(object Sender, IqEventArgs e)
		{
			foreach (XmlNode N in e.Query.ChildNodes)
			{
				if (N.LocalName == "message")
				{
					MessageEventArgs e2 = new MessageEventArgs(this, (XmlElement)N);

					e2.From = e.From;
					e2.To = e.To;

					this.SendIqResult(e.Id, e.From, string.Empty);
					this.ProcessMessage(e2);

					return;
				}
			}

			throw new BadRequestException(string.Empty, e.Query);
		}

		/// <summary>
		/// Maximum number of pending incoming assured messages received from a single source.
		/// </summary>
		public int MaxAssuredMessagesPendingFromSource
		{
			get { return this.maxAssuredMessagesPendingFromSource; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "MaxAssuredMessagesPendingFromSource");

				this.maxAssuredMessagesPendingFromSource = value;
			}
		}

		/// <summary>
		/// Maximum total number of pending incoming assured messages received.
		/// </summary>
		public int MaxAssuredMessagesPendingTotal
		{
			get { return this.maxAssuredMessagesPendingTotal; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "MaxAssuredMessagesPendingTotal");

				this.maxAssuredMessagesPendingTotal = value;
			}
		}

		/// <summary>
		/// Default retry timeout, in milliseconds.
		/// This value is used when sending IQ requests wihtout specifying request-specific retry parameter values.
		/// </summary>
		public int DefaultRetryTimeout
		{
			get { return this.defaultRetryTimeout; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "DefaultRetryTimeout");

				this.defaultRetryTimeout = value;
			}
		}

		/// <summary>
		/// Default number of retries if results or errors are not returned.
		/// This value is used when sending IQ requests wihtout specifying request-specific retry parameter values.
		/// </summary>
		public int DefaultNrRetries
		{
			get { return this.defaultNrRetries; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "DefaultNrRetries");

				this.defaultNrRetries = value;
			}
		}

		/// <summary>
		/// Default maximum retry timeout, in milliseconds.
		/// This value is used when sending IQ requests wihtout specifying request-specific retry parameter values.
		/// </summary>
		public int DefaultMaxRetryTimeout
		{
			get { return this.defaultMaxRetryTimeout; }
			set
			{
				if (value <= 0)
					throw new ArgumentException("Value must be positive.", "DefaultMaxRetryTimeout");

				this.defaultMaxRetryTimeout = value;
			}
		}

		/// <summary>
		/// Default Drop-off value. If drop-off is used, the retry timeout is doubled for each retry, up till the maximum retry timeout time.
		/// This value is used when sending IQ requests wihtout specifying request-specific retry parameter values.
		/// </summary>
		public bool DefaultDropOff
		{
			get { return this.defaultDropOff; }
			set { this.defaultDropOff = value; }
		}

		private void AssuredQoSMessageHandler(object Sender, IqEventArgs e)
		{
			string FromBareJid = GetBareJID(e.From);
			string MsgId = XML.Attribute(e.Query, "msgId");

			foreach (XmlNode N in e.Query.ChildNodes)
			{
				if (N.LocalName == "message")
				{
					MessageEventArgs e2 = new MessageEventArgs(this, (XmlElement)N);
					int i;

					e2.From = e.From;
					e2.To = e.To;

					lock (this.roster)
					{
						if (this.nrAssuredMessagesPending >= this.maxAssuredMessagesPendingTotal)
						{
							Log.Warning("Rejected incoming assured message. Unable to manage more than " + this.maxAssuredMessagesPendingTotal.ToString() +
								" pending assured messages.", GetBareJID(e.To), GetBareJID(e.From), "ResourceConstraint",
								new KeyValuePair<string, object>("Variable", "NrAssuredMessagesPending"),
								new KeyValuePair<string, object>("Limit", (double)this.maxAssuredMessagesPendingTotal),
								new KeyValuePair<string, object>("Unit", string.Empty));

							throw new StanzaErrors.ResourceConstraintException(string.Empty, e.Query);
						}

						if (!this.roster.ContainsKey(FromBareJid))
						{
							Log.Notice("Rejected incoming assured message. Sender not in roster.", GetBareJID(e.To), GetBareJID(e.From), "NotAllowed",
								new KeyValuePair<string, object>("Variable", "NrAssuredMessagesPending"));

							throw new NotAllowedException(string.Empty, e.Query);
						}

						if (this.pendingAssuredMessagesPerSource.TryGetValue(FromBareJid, out i))
						{
							if (i >= this.maxAssuredMessagesPendingFromSource)
							{
								Log.Warning("Rejected incoming assured message. Unable to manage more than " + this.maxAssuredMessagesPendingFromSource.ToString() +
									" pending assured messages from each sender.", GetBareJID(e.To), GetBareJID(e.From), "ResourceConstraint",
									new KeyValuePair<string, object>("Variable", "NrPendingAssuredMessagesPerSource"),
									new KeyValuePair<string, object>("Limit", (double)this.maxAssuredMessagesPendingFromSource),
									new KeyValuePair<string, object>("Unit", string.Empty));

								throw new StanzaErrors.ResourceConstraintException(string.Empty, e.Query);
							}
						}
						else
							i = 0;

						i++;
						this.pendingAssuredMessagesPerSource[FromBareJid] = i;
						this.receivedMessages[FromBareJid + " " + MsgId] = e2;
					}

					this.SendIqResult(e.Id, e.From, "<received msgId='" + XML.Encode(MsgId) + "'/>");
					return;
				}
			}

			throw new BadRequestException(string.Empty, e.Query);
		}

		/// <summary>
		/// Gets the Bare JID from a JID, which may be a Full JID.
		/// </summary>
		/// <param name="JID">JID</param>
		/// <returns>Bare JID</returns>
		public static string GetBareJID(string JID)
		{
			int i = JID.IndexOf('/');
			if (i > 0)
				return JID.Substring(0, i);
			else
				return JID;
		}

		private void DeliverQoSMessageHandler(object Sender, IqEventArgs e)
		{
			MessageEventArgs e2;
			string MsgId = XML.Attribute(e.Query, "msgId");
			string From = GetBareJID(e.From);
			string Key = From + " " + MsgId;
			int i;

			lock (this.roster)
			{
				if (this.receivedMessages.TryGetValue(Key, out e2))
				{
					this.receivedMessages.Remove(Key);
					this.nrAssuredMessagesPending--;

					if (this.pendingAssuredMessagesPerSource.TryGetValue(From, out i))
					{
						i--;
						if (i <= 0)
							this.pendingAssuredMessagesPerSource.Remove(From);
						else
							this.pendingAssuredMessagesPerSource[From] = i;
					}
				}
				else
					e2 = null;
			}

			this.SendIqResult(e.Id, e.From, string.Empty);

			if (e2 != null)
				this.ProcessMessage(e2);
		}

		private void SecondTimerCallback(object State)
		{
			if (DateTime.Now >= this.nextPing)
			{
				this.nextPing = DateTime.Now.AddMilliseconds(this.keepAliveSeconds * 500);
				try
				{
					this.BeginWrite(" ", null);
				}
				catch (Exception ex)
				{
					this.Exception(ex);
					this.Reconnect();
				}
			}

			List<PendingRequest> Retries = null;
			DateTime Now = DateTime.Now;
			DateTime TP;
			bool Retry;

			lock (this.synchObject)
			{
				foreach (KeyValuePair<DateTime, PendingRequest> P in this.pendingRequestsByTimeout)
				{
					if (P.Key <= Now)
					{
						if (Retries == null)
							Retries = new List<PendingRequest>();

						Retries.Add(P.Value);
					}
					else
						break;
				}
			}

			if (Retries != null)
			{
				foreach (PendingRequest Request in Retries)
				{
					lock (this.synchObject)
					{
						this.pendingRequestsByTimeout.Remove(Request.Timeout);

						if (Retry = Request.CanRetry())
						{
							TP = Request.Timeout;

							while (this.pendingRequestsByTimeout.ContainsKey(TP))
								TP = TP.AddTicks(this.gen.Next(1, 10));

							Request.Timeout = TP;

							this.pendingRequestsByTimeout[Request.Timeout] = Request;
						}
						else
							this.pendingRequestsBySeqNr.Remove(Request.SeqNr);
					}

					try
					{
						if (Retry)
							this.BeginWrite(Request.Xml, null);
						else
						{
							StringBuilder Xml = new StringBuilder();

							Xml.Append("<iq xmlns='jabber:client' type='error' from='");
							Xml.Append(Request.To);
							Xml.Append("' id='");
							Xml.Append(Request.SeqNr.ToString());
							Xml.Append("'><error type='wait'><recipient-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>");
							Xml.Append("<text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>Timeout.</text></error></iq>");

							XmlDocument Doc = new XmlDocument();
							Doc.LoadXml(Xml.ToString());

							IqResultEventArgs e = new IqResultEventArgs(Doc.DocumentElement, Request.SeqNr.ToString(), string.Empty, Request.To, false,
								Request.State);

							IqResultEventHandler h = Request.Callback;
							if (h != null)
								h(this, e);
						}
					}
					catch (Exception ex)
					{
						this.Exception(ex);
					}
				}
			}
		}

	}
}