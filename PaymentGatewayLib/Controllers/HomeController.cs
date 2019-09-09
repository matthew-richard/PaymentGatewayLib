using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace PaymentGatewayAPI.Controllers
{
    public class HomeController : Controller
    {
        protected PaymentGateway paymentGateway = new PaymentGateway (
            terminalID: "0",
            programName: "Ski Area Gift Card",
            serverHost: "qa.paymentgateway.com",
            serverPort: 61235,
            minCardNumber: "0",
            maxCardNumber: "1"
        );

        public ActionResult Index()
        {
            if (HttpContext.Request.HttpMethod == "POST")
            {
                try
                {
                    string cardNumber = HttpContext.Request.Form["card-number"];
                    ViewBag.CardNumber = cardNumber;
                    switch (HttpContext.Request.Form["action"])
                    {
                        case "checkExistence":
                            bool exists = paymentGateway.AccountExists(cardNumber);
                            if (exists)
                            {
                                ViewBag.SuccessMessage = "Account exists!";
                            }
                            else
                            {
                                ViewBag.ErrorMessage = "Account does not exist.";
                            }
                            break;

                        case "charge":
                            double chargeAmount = Double.Parse(HttpContext.Request.Form["charge-amount"]);
                            try {
                                paymentGateway.Charge(cardNumber, chargeAmount);
                                ViewBag.SuccessMessage = "Account was successfully charged.";
                            } catch (PaymentGatewayException e)
                            {
                                ViewBag.ErrorMessage = e.Message;
                            }
                            break;

                        case "return":
                            double returnAmount = Double.Parse(HttpContext.Request.Form["return-amount"]);
                            paymentGateway.Deposit(cardNumber, returnAmount);
                            ViewBag.SuccessMessage = "Deposit successful.";
                            break;

                        case "ping":
                            bool result = paymentGateway.Ping();
                            if (result)
                            {
                                ViewBag.SuccessMessage = "Ping received OK!.";
                            }
                            else
                            {
                                ViewBag.ErrorMessage = "Ping did not receive OK :(.";
                            }
                            break;

                        case "balance":
                            decimal balance = paymentGateway.GetBalance(cardNumber);
                            ViewBag.SuccessMessage = "Balance: $" + balance;
                            break;

                        case "activate":
                            paymentGateway.ActivateAccount(cardNumber);
                            ViewBag.SuccessMessage = "Account activated!";
                            break;

                        case "create":
                            string newCardNumber = paymentGateway.CreateAccount();
                            ViewBag.SuccessMessage = "Account created! Card number: " + newCardNumber;
                            break;

                        default:
                            ViewBag.ErrorMessage = "Invalid action.";
                            break;

                    }
                } catch (FormatException e)
                {
                    ViewBag.ErrorMessage = "One or more inputs has an invalid format.";
                } catch (Exception e)
                {
                    ViewBag.ErrorMessage = e.GetType() + ":" + e.Message;
                }
            }

            return View();
        }
    }
}