using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace POE_CLOUD1.Controllers
{
    public class CartController : Controller
    {
        private List<CartItem> GetCart()
        {
            var data = HttpContext.Session.GetString("CART");
            return data == null ? new List<CartItem>() : JsonSerializer.Deserialize<List<CartItem>>(data) ?? new List<CartItem>();
        }

        private void SaveCart(List<CartItem> cart)
        {
            var json = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString("CART", json);
        }

        public IActionResult Index()
        {
            var cart = GetCart();
            ViewBag.Total = cart.Sum(c => c.LineTotal);
            return View(cart.OrderBy(c => c.ProductId).ToList());
        }

        [HttpPost]
        public IActionResult Update(int productId, int qty)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(c => c.ProductId == productId);
            if (item == null) return RedirectToAction(nameof(Index));

            if (qty <= 0)
            {
                cart.Remove(item);
            }
            else
            {
                var updated = item with { Quantity = qty };
                cart.Remove(item);
                cart.Add(updated);
            }

            SaveCart(cart);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Clear()
        {
            HttpContext.Session.Remove("CART");
            return RedirectToAction(nameof(Index));
        }
    }
}