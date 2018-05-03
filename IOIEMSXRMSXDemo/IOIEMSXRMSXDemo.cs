/* Copyright 2018. Bloomberg Finance L.P.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to
deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:  The above
copyright notice and this permission notice shall be included in all copies
or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
IN THE SOFTWARE.
*/

/* Demo application which brings together EasyMSX, EasyIOI and RuleMSX to show how we can 
 * use IOI data to automatically match staged orders, and route directly to the IOI issuing 
 * sell-side.
 * 
 * When a new IOI is recieved, we look to see if the security on the IOI can be found in the list of
 * orders in EMSX. Next we check if the side is correct (offer->BUY, bid->SELL). Finally, we check 
 * that the idle shares on the order are greater than or equal to the shares listed on the IOI. 
 * If this happens we create a route on the order for the amount shown in either ioi_bid_size_quantity
 * or ioi_offer_size_quantity, and route it to the broker code shown in ioi_route_customId. 
 */


using System;

using com.bloomberg.samples.rulemsx;
using LogRmsx = com.bloomberg.samples.rulemsx.Log;
using Action = com.bloomberg.samples.rulemsx.Action;

using com.bloomberg.emsx.samples;
using EMSXNotificationHandler = com.bloomberg.emsx.samples.NotificationHandler;
using EMSXNotification = com.bloomberg.emsx.samples.Notification;
using EMSXField = com.bloomberg.emsx.samples.Field;
using LogEMSX = com.bloomberg.emsx.samples.Log;

using com.bloomberg.ioiapi.samples;
using IOINotificationHandler = com.bloomberg.ioiapi.samples.NotificationHandler;
using IOINotification = com.bloomberg.ioiapi.samples.Notification;
using IOIField = com.bloomberg.ioiapi.samples.Field;
using LogIOI = com.bloomberg.ioiapi.samples.Log;

namespace IOIEMSXRMSXDemo
{
    class IOIEMSXRMSXDemo : EMSXNotificationHandler, IOINotificationHandler
    {

        RuleMSX rmsx;
        EasyMSX emsx;
        EasyIOI eioi;

        static void Main(string[] args)
        {
            IOIEMSXRMSXDemo Test = new IOIEMSXRMSXDemo();
            Test.Run();

            log("Press enter to terminate...");
            System.Console.ReadLine();

            Test.Stop();

            System.Console.WriteLine("Terminating.");

        }

        private static void log(String msg)
        {
            System.Console.WriteLine(DateTime.Now.ToString("yyyyMMddHHmmssfffzzz") + "(RMSXIOITracking..): \t" + msg);
        }

        private void Run()
        {

            log("IOIEMSXRMSXDemo - Auto-routing IOI orders");

            log("Initializing RuleMSX...");
            this.rmsx = new RuleMSX();
            LogRmsx.logLevel = LogRmsx.LogLevels.NONE;
            LogRmsx.logPrefix = "(RuleMSX...)";

            log("RuleMSX initialized.");

            log("Initializing EasyIOI...");
            this.eioi = new EasyIOI();
            log("EasyIOI initialized.");

            LogIOI.logPrefix = "(EasyIOI...)";
            LogIOI.logLevel = com.bloomberg.ioiapi.samples.Log.LogLevels.NONE;

            log("Initializing EasyMSX...");
            this.emsx = new EasyMSX();
            log("EasyMSX initialized.");

            LogEMSX.logPrefix = "(EasyMSX...)";
            LogEMSX.logLevel = com.bloomberg.emsx.samples.Log.LogLevels.NONE;

            log("Create ruleset...");
            BuildRules();
            log("Ruleset ready.");

            this.emsx.orders.addNotificationHandler(this);
            this.emsx.routes.addNotificationHandler(this);

            log("Starting EasyMSX");
            this.emsx.start();
            log("EasyMSX started");

            this.eioi.iois.addNotificationHandler(this);

            log("Starting EasyIOI");
            this.eioi.start();
            log("EasyIOI started");

        }

        private void Stop()
        {
            log("Stopping RuleMSX");
            this.rmsx.Stop();
            log("RuleMSX stopped");
        }


        private void BuildRules()
        {

            log("Building rules...");

            log("Creating RuleSet rsOrderStates");
            RuleSet rsOrderStates = this.rmsx.CreateRuleSet("OrderStates");

            log("Creating rule for ORDER_NEW");
            Rule ruleOrderNew = rsOrderStates.AddRule("OrderNew");
            ruleOrderNew.AddRuleCondition(new RuleCondition("OrderNew", new OrderNew()));
            ruleOrderNew.AddAction(this.rmsx.CreateAction("ShowOrderNew", new ShowOrderState(this, "New Order")));



        }


        //EasyIOI Notification
        public void ProcessNotification(IOINotification notification)
        {
            if (notification.category == IOINotification.NotificationCategory.IOIDATA && (notification.type == IOINotification.NotificationType.NEW)) this.parseIOI(notification.GetIOI());
        }

        //EasyMSX Notification
        public void ProcessNotification(EMSXNotification notification)
        {
            if ((notification.category == EMSXNotification.NotificationCategory.ORDER) && (notification.type != EMSXNotification.NotificationType.UPDATE)) this.parseOrder(notification.getOrder());
        }

        public void parseIOI(IOI i)
        {
            DataSet newDataSet = this.rmsx.CreateDataSet("DS_IOI_" + i.field("id_value").Value());
            newDataSet.AddDataPoint("handle", new IOIFieldDataPointSource(i, i.field("id_value")));
            newDataSet.AddDataPoint("change", new IOIFieldDataPointSource(i, i.field("change")));
            newDataSet.AddDataPoint("ticker", new IOIFieldDataPointSource(i, i.field("ioi_instrument_stock_security_ticker")));
            newDataSet.AddDataPoint("type", new IOIFieldDataPointSource(i, i.field("ioi_instrument_type")));
            newDataSet.AddDataPoint("bid_quantity", new IOIFieldDataPointSource(i, i.field("ioi_bid_size_quantity")));
            newDataSet.AddDataPoint("offer_quantity", new IOIFieldDataPointSource(i, i.field("ioi_offer_size_quantity")));
            
            this.rmsx.GetRuleSet("IOI").Execute(newDataSet);
        }

        public void parseOrder(Order o)
        {
            DataSet newDataSet = this.rmsx.CreateDataSet("DS_OR_" + o.field("EMSX_SEQUENCE").value());
            newDataSet.AddDataPoint("OrderStatus", new EMSXFieldDataPointSource(o.field("EMSX_STATUS")));
            newDataSet.AddDataPoint("OrderNumber", new EMSXFieldDataPointSource(o.field("EMSX_SEQUENCE")));
            newDataSet.AddDataPoint("OrderWorking", new EMSXFieldDataPointSource(o.field("EMSX_WORKING")));
            newDataSet.AddDataPoint("OrderAmount", new EMSXFieldDataPointSource(o.field("EMSX_AMOUNT")));
            newDataSet.AddDataPoint("OrderIdleAmount", new EMSXFieldDataPointSource(o.field("EMSX_IDLE_AMOUNT")));
            newDataSet.AddDataPoint("OrderTicker", new EMSXFieldDataPointSource(o.field("EMSX_TICKER")));
            newDataSet.AddDataPoint("OrderSide", new EMSXFieldDataPointSource(o.field("EMSX_SIDE")));

            this.rmsx.GetRuleSet("EMSX").Execute(newDataSet);
        }


        class IOIFieldDataPointSource : DataPointSource, IOINotificationHandler
        {
            IOIField field;
            String value;

            internal IOIFieldDataPointSource(IOI i, IOIField field)
            {
                this.field = field;
                this.value = field.Value();

                i.addNotificationHandler(this);

            }

            public override object GetValue()
            {
                return this.field.Value();
            }

            public object GetPreviousValue()
            {
                return this.field.previousValue();
            }

            public void ProcessNotification(IOINotification notification)
            {
                if (this.field.previousValue() != this.field.Value()) this.SetStale();
            }

        }

        class EMSXFieldDataPointSource : DataPointSource, EMSXNotificationHandler
        {
            EMSXField field;
            String value;

            internal EMSXFieldDataPointSource(EMSXField field)
            {
                this.field = field;
                this.value = field.value();

                this.field.addNotificationHandler(this);

            }

            public override object GetValue()
            {
                return this.field.value();
            }

            public object GetPreviousValue()
            {
                return this.field.previousValue();
            }

            public void ProcessNotification(EMSXNotification notification)
            {
                if (this.field.previousValue() != this.field.value()) this.SetStale();
            }
        }

    }
}
