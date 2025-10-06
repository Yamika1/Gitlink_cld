using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AzureFunctions;

public class Function1
{
    private readonly ILogger<Function1> _logger;

    public Function1(ILogger<Function1> logger)
    {
        _logger = logger;
    }

    [Function("Function1")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
    [Function("Orders")]
    public async Task<IActionResult> Order([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request");
        string OrderName = null;
        string OrderDescription = null;
        string OrderType = null;
        OrderName = req.Query["name"];
        OrderDescription = req.Query["description"];
        OrderType = req.Query["type"];

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        string responseMessage;
        if ((OrderName == null) || (OrderDescription == null) || (OrderType == null))
        {
            responseMessage = "please enter in the name, description and type of the order";
        }
        else
        {
            responseMessage = $"{OrderName} has the description of: {OrderDescription} which is a {OrderType}";
        }
        return new OkObjectResult(responseMessage);
    }
    [Function("Products")]
    public async Task<IActionResult> Product([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request");
        string ProductName = null;
        string ProductDescription = null;
        string ProductType = null;
        string ProductPrice = null;
        ProductName = req.Query["name"];
        ProductDescription = req.Query["description"];
        ProductType = req.Query["type"];
        ProductPrice = req.Query["price"];

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        string responseMessage;
        if ((ProductName == null) || (ProductDescription == null) || (ProductType == null) || (ProductPrice == null))
        {
            responseMessage = "please enter in the name, description, type and price of the product";
        }
        else
        {
            responseMessage = $"{ProductName} has the description of: {ProductDescription} which is a {ProductType} at the price of {ProductPrice}";
        }
        return new OkObjectResult(responseMessage);
    }
    [Function("Customers")]
    public async Task<IActionResult> Customer([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request");
        string CustomerName = null;
        string CustomerSurname = null;
        string CustomerEmail = null;
        CustomerName = req.Query["name"];
        CustomerSurname = req.Query["surname"];
        CustomerEmail = req.Query["email"];

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        string responseMessage;
        if ((CustomerName == null) || (CustomerSurname == null) || (CustomerEmail == null))
        {
            responseMessage = "please enter in the name, surname, age and email of the customer";
        }
        else
        {
            responseMessage = $"Hello {CustomerName} {CustomerSurname} , with the email of {CustomerEmail}";
        }
        return new OkObjectResult(responseMessage);
    }
}


