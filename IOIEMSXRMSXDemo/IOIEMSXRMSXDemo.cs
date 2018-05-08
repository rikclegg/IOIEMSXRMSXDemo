﻿/* Copyright 2018. Bloomberg Finance L.P.

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

            log("Creating RuleSet rsAutoRouteFromIOI");
            RuleSet rsAutoRouteFromIOI = this.rmsx.CreateRuleSet("AutoRouteFromIOI");

            log("Creating rule for ValidIOI");
            Rule ruleValidIOI = rsAutoRouteFromIOI.AddRule("ValidIOI");

            /* ioi must be instrument type 'stock'
             * ioi must not be expired */

            ruleValidIOI.AddRuleCondition(new RuleCondition("IsStockInstrumentType", new GenericStringMatch("ioitype","stock")));
            ruleValidIOI.AddRuleCondition(new RuleCondition("IsNotExpired", new IsIOINotExpired()));

            ruleValidIOI.AddAction(this.rmsx.CreateAction("MarkIOIValid", ActionType.ON_TRUE, new MarkIOIValid()));

            Action purgeDataSet = this.rmsx.CreateAction("PurgeDataSet", ActionType.ON_FALSE, new PurgeDataSet(rsAutoRouteFromIOI));
            ruleValidIOI.AddAction(purgeDataSet);

            log("Creating rule for ValidOrder");
            Rule ruleValidOrder = rsAutoRouteFromIOI.AddRule("ValidOrder");

            /* Order must be for Equity */

            ruleValidOrder.AddRuleCondition(new RuleCondition("IsEquity", new GenericStringMatch("orderAssetClass", "Equity")));

            ruleValidOrder.AddAction(this.rmsx.CreateAction("MarkOrderValid", ActionType.ON_TRUE, new MarkOrderValid()));
            ruleValidOrder.AddAction(purgeDataSet);

            log("Creating rule for ValidPair");
            Rule ruleValidPair = rsAutoRouteFromIOI.AddRule("ValidPair");
            /* both order and ioi must be valid
             * matching side
             */

            ruleValidPair.AddRuleCondition(new RuleCondition("IOIAndOrderReady", new IOIAndOrderReady()));
            ruleValidPair.AddRuleCondition(new RuleCondition("MatchingSide", new MatchingSideAndAmount()));
            
            /* Create new route */

            ruleValidPair.AddAction(this.rmsx.CreateAction("CreateRoute"), ActionType.ON_TRUE, new CreateRoute()));
            ruleValidOrder.AddAction(purgeDataSet);

        }

        //EasyIOI Notification
        public void ProcessNotification(IOINotification notification)
        {
            if (notification.category == IOINotification.NotificationCategory.IOIDATA && (notification.type == IOINotification.NotificationType.NEW))
            {

                //Create conflict set with all current orders.
                IOI i = notification.GetIOI();

                foreach (Order o in emsx.orders)
                {
                    CreateConflictDataSet(i, o);
                }
            }
        }

        //EasyMSX Notification
        public void ProcessNotification(EMSXNotification notification)
        {
            if ((notification.category == EMSXNotification.NotificationCategory.ORDER) && (notification.type != EMSXNotification.NotificationType.UPDATE))
            {
                //Create conflict set with all current orders.
                Order o = notification.getOrder();

                foreach (IOI i in eioi.iois)
                {
                    CreateConflictDataSet(i, o);
                }
            }
        }

        public void CreateConflictDataSet(IOI i, Order o)
        {
            DataSet newDataSet = this.rmsx.CreateDataSet("conflict_" + i.field("id_value").Value() + o.field("EMSX_SEQUENCE").value());

            newDataSet.AddDataPoint("ioihandle", new IOIFieldDataPointSource(i, i.field("id_value")));
            newDataSet.AddDataPoint("ioichange", new IOIFieldDataPointSource(i, i.field("change")));
            newDataSet.AddDataPoint("ioiticker", new IOIFieldDataPointSource(i, i.field("ioi_instrument_stock_security_ticker")));
            newDataSet.AddDataPoint("ioitype", new IOIFieldDataPointSource(i, i.field("ioi_instrument_type")));
            newDataSet.AddDataPoint("ioigooduntil", new IOIFieldDataPointSource(i, i.field("ioi_goodUntil")));
            newDataSet.AddDataPoint("ioibidquantity", new IOIFieldDataPointSource(i, i.field("ioi_bid_size_quantity")));
            newDataSet.AddDataPoint("ioiofferquantity", new IOIFieldDataPointSource(i, i.field("ioi_offer_size_quantity")));
            newDataSet.AddDataPoint("ioiisvalid", new GenericBooleanSource(false));

            newDataSet.AddDataPoint("orderStatus", new EMSXFieldDataPointSource(o.field("EMSX_STATUS")));
            newDataSet.AddDataPoint("orderNumber", new EMSXFieldDataPointSource(o.field("EMSX_SEQUENCE")));
            newDataSet.AddDataPoint("orderWorking", new EMSXFieldDataPointSource(o.field("EMSX_WORKING")));
            newDataSet.AddDataPoint("orderAmount", new EMSXFieldDataPointSource(o.field("EMSX_AMOUNT")));
            newDataSet.AddDataPoint("orderIdleAmount", new EMSXFieldDataPointSource(o.field("EMSX_IDLE_AMOUNT")));
            newDataSet.AddDataPoint("orderTicker", new EMSXFieldDataPointSource(o.field("EMSX_TICKER")));
            newDataSet.AddDataPoint("orderSide", new EMSXFieldDataPointSource(o.field("EMSX_SIDE")));
            newDataSet.AddDataPoint("orderAssetClass", new EMSXFieldDataPointSource(o.field("EMSX_ASSET_CLASS")));
            newDataSet.AddDataPoint("orderisvalid", new GenericBooleanSource(false));

            this.rmsx.GetRuleSet("AutoRouteFromIOI").Execute(newDataSet);
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

        class GenericBooleanSource : DataPointSource
        {
            Boolean value;

            internal GenericBooleanSource(Boolean initialValue)
            {
                SetValue(initialValue);
            }

            public override object GetValue()
            {
                return this.value;
            }

            public void SetValue(Boolean newValue)
            {
                this.value = newValue;
                this.SetStale();
            }
        }

        class GenericStringMatch : RuleEvaluator
        {
            String sourceName;
            String targetValue;

            public GenericStringMatch(String sourceName, String targetValue)
            {
                this.sourceName = sourceName;
                this.targetValue = targetValue;
                this.AddDependantDataPointName(sourceName);
            }

            public override bool Evaluate(DataSet dataSet)
            {
                DataPointSource source = dataSet.GetDataPoint(this.sourceName).GetSource();

                String currentValue = Convert.ToString(source.GetValue());

                return (currentValue == this.targetValue);
            }
        }

        class IsIOINotExpired : RuleEvaluator
        {
            public IsIOINotExpired()
            {
                this.AddDependantDataPointName("ioigooduntil");
            }

            public override bool Evaluate(DataSet dataSet)
            {
                DataPointSource source = dataSet.GetDataPoint("ioigooduntil").GetSource();

                DateTime currentValue = Convert.ToDateTime(source.GetValue());

                return (currentValue < DateTime.Now);
            }
        }

        class MarkIOIValid : ActionExecutor
        {
            public void Execute(DataSet dataSet)
            {
                GenericBooleanSource dps = (GenericBooleanSource)dataSet.GetDataPoint("ioiisvalid").GetSource();
                dps.SetValue(true);
            }
        }

        class MarkOrderValid : ActionExecutor
        {
            public void Execute(DataSet dataSet)
            {
                GenericBooleanSource dps = (GenericBooleanSource)dataSet.GetDataPoint("orderisvalid").GetSource();
                dps.SetValue(true);
            }
        }

        class PurgeDataSet :  ActionExecutor
        {
            RuleSet ruleSet;

            internal PurgeDataSet(RuleSet ruleSet)
            {
                this.ruleSet = ruleSet;
            }

            public void Execute(DataSet dataSet)
            {
                this.ruleSet.PurgeDataSet(dataSet);
            }
        }

        class HasIdleShares :  RuleEvaluator
        {
            public override bool Evaluate(DataSet dataSet)
            {
                EMSXFieldDataPointSource idleSource = (EMSXFieldDataPointSource)dataSet.GetDataPoint("OrderIdleAmount").GetSource();
                int idleAmount = Convert.ToInt32(idleSource.GetValue());
                return (idleAmount > 0);
            }
        }

        class IOIAndOrderReady : RuleEvaluator
        {
            public override bool Evaluate(DataSet dataSet)
            {
                GenericBooleanSource ioiSource = (GenericBooleanSource)dataSet.GetDataPoint("ioiisvalid").GetSource();
                GenericBooleanSource ordSource = (GenericBooleanSource)dataSet.GetDataPoint("orderisvalid").GetSource();

                bool ioiValid = Convert.ToBoolean(ioiSource.GetValue());
                bool ordValid = Convert.ToBoolean(ordSource.GetValue());

                return (ioiValid && ordValid);
            }
        }

        class MatchingSideAndAmount : RuleEvaluator
        {
            public override bool Evaluate(DataSet dataSet)
            {
                IOIFieldDataPointSource offerQtySource = (IOIFieldDataPointSource)dataSet.GetDataPoint("ioiofferquantity").GetSource();
                IOIFieldDataPointSource bidQtySource = (IOIFieldDataPointSource)dataSet.GetDataPoint("ioibidquantity").GetSource();
                EMSXFieldDataPointSource orderSideSource = (EMSXFieldDataPointSource)dataSet.GetDataPoint("orderSide").GetSource();
                EMSXFieldDataPointSource orderIdleAmountSource = (EMSXFieldDataPointSource)dataSet.GetDataPoint("orderIdleAmount").GetSource();

                String orderSide = orderSideSource.GetValue().ToString();
                int orderIdleAmount = Convert.ToInt32(orderIdleAmountSource.GetValue());
                int bidQty = Convert.ToInt32(bidQtySource.GetValue());
                int offerQty = Convert.ToInt32(offerQtySource.GetValue());

                return ((bidQty > orderIdleAmount && orderSide == "BUY") || (offerQty > orderIdleAmount && orderSide == "SELL"));
            }
        }

        class CreateRoute : ActionExecutor
        {
            public void Execute(DataSet dataSet)
            {
             
            }
        }
    }
}
