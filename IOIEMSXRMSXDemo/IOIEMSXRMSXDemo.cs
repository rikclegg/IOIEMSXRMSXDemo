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

namespace IOIEMSXRMSXDemo
{
    class IOIEMSXRMSXDemo
    {
        static void Main(string[] args)
        {
        }
    }
}
