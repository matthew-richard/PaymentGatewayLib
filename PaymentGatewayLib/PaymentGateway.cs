using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace PaymentGatewayAPI
{
    public class PaymentGateway
    {
        /// <summary>
        /// PaymentGateway terminal ID
        /// </summary>
        public string TerminalID { get; set; }

        /// <summary>
        /// The exact name of the gift card program. If a card exists but does not belong
        /// to this program, AccountExists() returns false.
        /// </summary>
        public string ProgramName { get; set; }

        /// <summary>
        /// Host name of API server
        /// </summary>
        public string ServerHost { get; set; }

        /// <summary>
        /// Port of API server
        /// </summary>
        public int ServerPort { get; set; }

        /// <summary>
        /// How long to wait, in milliseconds, before abandoning connection attempt
        /// </summary>
        public int TimeoutMilliseconds { get; set; } = 5 * 1000;

        /// <summary>
        /// Lower bound on card numbers in the PaymentGateway program
        /// </summary>
        public string MinCardNumber { get; set; }

        /// <summary>
        /// Upper bound on card numbers in the PaymentGateway program
        /// </summary>
        public string MaxCardNumber { get; set; }

        /// <summary>
        /// Constructs a new instance of the PaymentGateway API client.
        /// </summary>
        public PaymentGateway(string terminalID, string programName, string serverHost, int serverPort, string minCardNumber, string maxCardNumber)
        {
            TerminalID = terminalID;
            ProgramName = programName;
            ServerHost = serverHost;
            ServerPort = serverPort;
            MinCardNumber = minCardNumber;
            MaxCardNumber = maxCardNumber;

            if (minCardNumber.Length != maxCardNumber.Length)
            {
                throw new PaymentGatewayException("Min and max card numbers are not of the same length.");
            }
            else
            {
                try
                {
                    long.Parse(minCardNumber);
                    long.Parse(maxCardNumber);
                }
                catch (FormatException e)
                {
                    throw new PaymentGatewayException("Min or max card number is non-numeric.");
                }
            }
        }

        /// <summary>True if "OK" received, false if not.</summary>
        public Boolean Ping()
        {
            string xml =
                new XElement("PaymentGateway_PingRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID))
                ).ToString();

            var response = SendXMLMessage(xml, false);

            return response.Root.Element("PingResult").Attribute("Host").Value == "OK";
        }

        /// <summary>True if account exists, false if not.</summary>
        /// <param name="cardNumber">Account number</param>
        public Boolean AccountExists(string cardNumber)
        {

            string xml =
                new XElement("PaymentGateway_PaymentCardAdminRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID)),
                    new XElement("AdminRequest",
                        new XAttribute("RequestType", "Account Info"),
                        new XElement("PaymentCard",
                            new XAttribute("CardNumber", cardNumber)))
                ).ToString();

            XDocument response = null;
            try
            {
                response = SendXMLMessage(xml);
            }
            catch (PaymentGatewayException e)
            {
                // No 'success' element; account doesn't exist.
                return false;
            }

            // Account exists only if it's marked as 'Active' and belongs to our program.
            return (response.Root.Element("AdminResult").Attribute("Status").Value == "Active")
                && (response.Root.Element("AdminResult").Attribute("ProgramName").Value == ProgramName);
        }

        /// <summary>
        /// Activates the specified card number. Initial balance of card is $0.
        /// </summary>
        /// /// <param name="cardNumber">Account number</param>
        public void ActivateAccount(string cardNumber)
        {
            if (AccountExists(cardNumber))
            {
                throw new PaymentGatewayException(String.Format("Cannot activate PaymentGateway account because it is already active.", cardNumber));
            }

            string xml =
                new XElement("PaymentGateway_PaymentCardAdminRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID)),
                    new XElement("AdminRequest",
                        new XAttribute("RequestType", "Activate"),
                        new XAttribute("Amount", "0.00"),
                        new XAttribute("RefNumber", new Random().Next(1000)),
                        new XElement("PaymentCard",
                            new XAttribute("CardNumber", cardNumber)))
                ).ToString();

            var response = SendXMLMessage(xml);
            bool accountActive = response.Root.Element("AdminResult").Attribute("Status").Value == "Active";
            decimal balance = Decimal.Parse(response.Root.Element("AdminResult").Attribute("BalanceRemaining").Value);
            bool accountEmpty = (balance == 0);

            if (!accountActive)
            {
                throw new PaymentGatewayException(String.Format(
                    "PaymentGateway returned an unexpected response. Response included \"Success\" "
                    + "element, but account status was not listed as \"Active\".", cardNumber), response);
            }
            else if (!accountEmpty)
            {
                // Empty newly created account if PaymentGateway automatically starts it with a nonzero balance.
                try
                {
                    Charge(cardNumber, (double)balance);
                }
                catch (PaymentGatewayException e)
                {
                    throw new PaymentGatewayException("Error zeroing out newly created account. Details: " + e.Message, e.Response);
                }
            }
        }

        /// <summary>
        /// Finds an unissued card number, activates the corresponding account, and returns
        /// the account number.
        /// </summary>
        public string CreateAccount()
        {
            /* Since we can't list all active account numbers in a program, we generate a
             * random card number and attempt to activate it. */
            long min = long.Parse(MinCardNumber);
            long max = long.Parse(MaxCardNumber);
            int range = (int) (max - min);

            try
            {
                Random rand = new Random();
                string cardNumber;
                do
                {
                    cardNumber = (min + (long)rand.Next(range)).ToString();
                }
                while (AccountExists(cardNumber));

                ActivateAccount(cardNumber);

                return cardNumber;
            }
            catch (PaymentGatewayException e)
            {
                throw new PaymentGatewayException("Error while finding and activating a new account. Details: " + e.Message, e.Response);
            }
        }

        /// <summary>
        /// Returns account balance.
        /// </summary>
        /// <param name="cardNumber">Account number</param>
        public Decimal GetBalance(string cardNumber)
        {
            string xml =
                new XElement("PaymentGateway_PaymentCardAdminRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID)),
                    new XElement("AdminRequest",
                        new XAttribute("RequestType", "Account Info"),
                        new XElement("PaymentCard",
                            new XAttribute("CardNumber", cardNumber)))
                ).ToString();

            var response = SendXMLMessage(xml);

            return Decimal.Parse(response.Root.Element("AdminResult").Attribute("BalanceRemaining").Value);
        }

        /// <summary>Charges card with the specified amount. Throws PaymentGatewayException if balance is too low.</summary>
        /// <param name="cardNumber">Account number to be charged</param>
        /// <param name="amount">Amount to charge, in USD</param>
        public void Charge(string cardNumber, double amount)
        {
            // Check account balance before charging. If there's not enough, abort the transaction
            // and return false--we don't want any partial approvals.
            if (GetBalance(cardNumber) < (decimal)amount)
                throw new PaymentGatewayException("Charge failed because account balance was too low.");

            string xml =
                new XElement("HTNG_PaymentCardProcessingRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID)),
                    new XElement("AuthorizationDetail",
                        new XAttribute("RefNumber", new Random().Next(1000)),
                        new XElement("CreditCardAuthorization",
                            new XAttribute("TransactionType", "Sale"),
                            new XAttribute("Amount", Math.Round(amount,2).ToString("f2")),
                            new XAttribute("CardPresentInd", "True"),
                            new XElement("CreditCard",
                                new XAttribute("CardNumber", cardNumber),
                                new XAttribute("CardNumberIsPrivateLabel", "True"))))
                ).ToString();

            var response = SendXMLMessage(xml);

            if (response.Root.Element("Authorization").Element("AuthorizationResult").Attribute("Result").Value != "APPROVED")
                throw new PaymentGatewayException("Charge was not approved for some reason. Response contents attached.", response);

            return;
        }

        /// <summary>Deposits money into specified account.</summary>
        /// <param name="cardNumber">Account number to deposit into</param>
        /// <param name="amount">Amount to deposit, in USD</param>
        public void Deposit(string cardNumber, double amount)
        {
            string xml =
                new XElement("HTNG_PaymentCardProcessingRQ",
                    new XAttribute("TimeStamp", XmlConvert.ToString(DateTime.Now, XmlDateTimeSerializationMode.Utc)),
                    new XAttribute("Version", "1.000"),
                    new XElement("POS",
                        new XAttribute("TerminalID", TerminalID)),
                    new XElement("AuthorizationDetail",
                        new XAttribute("RefNumber", new Random().Next(1000)),
                        new XElement("CreditCardAuthorization",
                            new XAttribute("TransactionType", "Return"),
                            new XAttribute("Amount", Math.Round(amount, 2).ToString("f2")),
                            new XAttribute("CardPresentInd", "True"),
                            new XElement("CreditCard",
                                new XAttribute("CardNumber", cardNumber),
                                new XAttribute("CardNumberIsPrivateLabel", "True"))))
                ).ToString();

            var response = SendXMLMessage(xml);

            if (response.Root.Element("Authorization").Element("AuthorizationResult").Attribute("Result").Value != "APPROVED")
                throw new PaymentGatewayException("Deposit was not approved for some reason. Response contents attached.", response);

            return;
        }

        /// <summary>
        /// Sends XML to PaymentGateway API and returns the
        /// response message (without the root element). Returns null if the connection failed.
        /// </summary>
        /// <param name="xml">XML to send</param>
        /// <param name="requireSuccessElement">Whether to throw an exception if no 'Success' element is found in the response. Defaults to true (exception thrown).</param>
        private XDocument SendXMLMessage(string xml, bool requireSuccessElement = true)
        {
            try
            {
                using (TcpClient client = new TcpClient(ServerHost, ServerPort))
                using (Stream stream = client.GetStream())
                {
                    client.ReceiveTimeout = TimeoutMilliseconds;

                    // Send XML message
                    stream.Write(System.Text.Encoding.ASCII.GetBytes(xml), 0, xml.Length);

                    // Parse response
                    var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true };
                    XDocument doc = null;
                    using (XmlReader reader = XmlReader.Create(stream, settings))
                    {
                        reader.MoveToContent();
                        doc = XDocument.Load(reader.ReadSubtree());
                    }

                    if (requireSuccessElement && doc.Root.Element("Success") == null)
                        throw new PaymentGatewayException("The Payment Gateway API responded but did not include a success element.", doc);

                    return doc;
                }
            } catch (IOException e)
            {
                // Payment Gateway took too long to respond
                throw new IOException("Connection with Payment Gateway API timed out.", e);
            }

        }
    }
}
