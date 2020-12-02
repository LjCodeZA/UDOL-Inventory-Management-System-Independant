using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using InventoryManagementSystem.DAL;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.POCO;

namespace InventoryManagementSystem.Controllers
{
    public class StockOutController : Controller
    {
        private IMSContext db = new IMSContext();

        // GET: StockOuts
        public ActionResult Index()
        {
            return View(db.StockOut.ToList());
        }

        // GET: StockOuts/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            StockOut stockOut = db.StockOut.Find(id);
            if (stockOut == null)
            {
                return HttpNotFound();
            }
            return View(stockOut);
        }

        // GET: StockOuts/Create
        public ActionResult Create()
        {
            var vendorProduct = (from productVendor in db.ProductVendor
                                 join vendor in db.Vendor
                                   on productVendor.VendorId equals vendor.VendorId
                                 join product in db.Product
                                  on productVendor.ProductId equals product.ProductId
                                 select new SelectListItem
                                 {
                                     Value = productVendor.ProductVendorId.ToString(),
                                     Text = productVendor.ProductVendorId.ToString() + " - " + vendor.Name + " - " + product.Name
                                 }).ToList();

            ViewBag.ProductVendorId = vendorProduct;
            return View();
        }

        // POST: StockOuts/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "StockOutId,ProductVendorId,Quantity,Recon,CreatedDate")] StockOut stockOut)
        {
            if (ModelState.IsValid)
            {
                DoStockAllocation(stockOut);
                return RedirectToAction("Index");
            }

            return View(stockOut);
        }

        // GET: StockOuts/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            StockOut stockOut = db.StockOut.Find(id);
            if (stockOut == null)
            {
                return HttpNotFound();
            }
            return View(stockOut);
        }

        // POST: StockOuts/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "StockOutId,ProductVendorId,Quantity,CreatedDate")] StockOut stockOut)
        {
            if (ModelState.IsValid)
            {
                db.Entry(stockOut).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(stockOut);
        }

        // GET: StockOuts/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            StockOut stockOut = db.StockOut.Find(id);
            if (stockOut == null)
            {
                return HttpNotFound();
            }
            return View(stockOut);
        }

        // POST: StockOuts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            StockOut stockOut = db.StockOut.Find(id);
            db.StockOut.Remove(stockOut);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        public void DoStockAllocation(StockOut stockOut)
        {
            var dbContext = new IMSContext();

            var productId = (from productVendor in dbContext.ProductVendor where productVendor.ProductVendorId == stockOut.ProductVendorId select productVendor.ProductId).FirstOrDefault();
          
            var allVendorsForProduct = (from productVendors in dbContext.ProductVendor where productVendors.ProductId == productId select productVendors); //Exclude current selected vendor

            //Does the selected vendor have enough stock?
            var stockTakenOutButNotReconned = (from stockTakenOut in dbContext.StockOut where stockTakenOut.ProductVendorId == stockOut.ProductVendorId && (stockTakenOut.Recon == false || stockTakenOut.Recon == null) select (int?)stockTakenOut.Quantity).Sum() ?? 0;

            var latestStockTake = (from productStock in dbContext.ProductStock
                                   group productStock by productStock.ProductVendorId
                                   into latestStock
                                   select latestStock.OrderByDescending(t => t.StockTakeDate).FirstOrDefault());

            var amountOfStockReconnedForVendor = latestStockTake.Where(i => i.ProductVendorId == stockOut.ProductVendorId).Select(o => (int?)o.Quantity).Sum() ?? 0;

            if (amountOfStockReconnedForVendor - (stockTakenOutButNotReconned + stockOut.Quantity) < 0)
            {
                //Not enough stock for order from this vendor. Need to check different vendors for stock.
                //We start off by checking if we have enough in total.
                var stockTakenOutButNotReconnedForProductVendors = (from stockTakenOut in dbContext.StockOut where allVendorsForProduct.Select(i => i.ProductVendorId).Contains(stockTakenOut.ProductVendorId) && (stockTakenOut.Recon == false || stockTakenOut.Recon == null) select (int?)stockTakenOut.Quantity).Sum() ?? 0;
                var amountOfStockReconnedForProductVendors = latestStockTake.Where(i => allVendorsForProduct.Select(vendors => vendors.ProductVendorId).Contains(i.ProductVendorId)).Select(o => o.Quantity).Sum();

                if (amountOfStockReconnedForProductVendors - (stockTakenOutButNotReconnedForProductVendors + stockOut.Quantity) >= 0)
                {
                    //We have enough total stock to service the request

                    var totalStockRequested = stockOut.Quantity;
                    var remainingStockToAllocate = totalStockRequested;

                    //Start by allocating stock from chosen vendor

                    var currentVendorStock = amountOfStockReconnedForVendor - stockTakenOutButNotReconned;


                    stockOut.Quantity = currentVendorStock;
                    db.StockOut.Add(stockOut);
                    db.SaveChanges();

                    remainingStockToAllocate -= currentVendorStock;

                    var alternativeProductVendors = (from productVendors in dbContext.ProductVendor where productVendors.ProductVendorId != stockOut.ProductVendorId && productVendors.ProductId == productId select productVendors); //Exclude current selected vendor
                    foreach (var item in alternativeProductVendors)
                    {
                        var amountOfStockReconnedForAlternativeVendor = latestStockTake.Where(i => i.ProductVendorId == item.ProductVendorId).Select(o => o.Quantity).Sum();
                        var stockTakenOutButNotReconnedForAlternativeVendor = (from stockTakenOut in dbContext.StockOut where stockTakenOut.ProductVendorId == item.ProductVendorId && (stockTakenOut.Recon == false || stockTakenOut.Recon == null) select (int?)stockTakenOut.Quantity).Sum() ?? 0;

                        currentVendorStock = amountOfStockReconnedForAlternativeVendor - stockTakenOutButNotReconnedForAlternativeVendor;
                        if (currentVendorStock == 0)
                            continue;

                        if (currentVendorStock >= remainingStockToAllocate)
                        {
                            db.StockOut.Add(new StockOut()
                            {
                                ProductVendorId = item.ProductVendorId,
                                CreatedDate = DateTime.Now,
                                Quantity = remainingStockToAllocate,
                                Recon = null
                            });

                            db.SaveChanges();

                            break;
                        }
                        else
                        {
                            db.StockOut.Add(new StockOut()
                            {
                                ProductVendorId = item.ProductVendorId,
                                CreatedDate = DateTime.Now,
                                Quantity = currentVendorStock,
                                Recon = null
                            });

                            db.SaveChanges();
                        }

                        remainingStockToAllocate -= currentVendorStock;
                    }
                }
                else
                {
                    //We don't have enough stock at all. Stop process.
                    return;
                }
            }
            else
            {
                //Enough stock from the selected vendor. Carry on with order.

                db.StockOut.Add(stockOut);
                db.SaveChanges();
                //return RedirectToAction("Index");
            }
        }
    }
}
