using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(PaymentGatewayAPI.Startup))]
namespace PaymentGatewayAPI
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            //ConfigureAuth(app);
        }
    }
}
