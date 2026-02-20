using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

[Route("api/v1/account/products")]
[Controller]
[Authorize(Policy = "Scope:PaymentInfo")]
public class AccountProductsController(IUserRepository userRepo, IProductRepository productRepo) : ControllerManager {

    [HttpGet]
    public ActionResult<SerbleProduct> Get() {
        User? target = HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return Ok(ProductManager.ListOfProductsFromUser(target, productRepo));
    }
}