using System;
using System.Runtime.Serialization;
using System.Xml.Linq;

namespace PaymentGatewayAPI
{
    [Serializable]
    class PaymentGatewayException : Exception
    {
        public XDocument Response { get; set; }

        public PaymentGatewayException(string message) : base(message)
        {
        }

        public PaymentGatewayException(string message, XDocument response) : base(message)
        {
            Response = response;
        }
    }
}
