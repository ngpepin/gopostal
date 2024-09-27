/// <summary>
/// ========================= Application Overview =========================
/// This application calculates shipping costs using the Canada Post API. 
/// It takes command line arguments for package dimensions, weight, and 
/// destination address. Optionally, a "from" address can be provided; 
/// otherwise, a default "from" address is used from the configuration file.
///
/// USAGE:
/// -w <width> -l <length> -h <height> -m <weight> -t '<to address>' [-f '<from address>']
/// 
/// /// Example:
/// dotnet run -w 10 -l 20 -h 15 -m 500 -t '123 Main St, Anytown, ON, A1A1A1' -f '456 Elm St, Othertown, BC, B2B2B2'
///
/// DEPENDENCIES:
/// - System.Net.Http: For making HTTP requests to external APIs.
/// - Newtonsoft.Json: For JSON parsing and serialization.
/// - System.Xml.Linq: For XML manipulation.
///
/// CONFIGURATION:
/// The application creates a configuration file located at:
/// $HOME/.config/gopostal/gopostal.conf
/// 
/// This configuration file can be subsequently edited by the user.
///
/// The configuration file contains the following JSON structure:
/// {
///   "CanadaPost": {
///     "ApiKey": "your_api_key",
///     "Secret": "your_secret",
///     "CustomerNumber": "your_customer_number",
///     "Endpoint": "https://ct.soa-gw.canadapost.ca/rs/ship/price",
///     "DefaultFromAddress": {
///       "Street": "your_street",
///       "City": "your_city",
///       "Province": "your_province",
///       "PostalCode": "your_postal_code"
///     }
///   }
/// }
///
/// However, before running the application, a `secrets.cs` file must be created.
/// This file is excluded from version control via `.gitignore` and must contain
/// the required secrets and API keys to initially populate the configuration file
/// JSON structure correctly.
///
/// --------------------- Steps to Create `secrets.cs` ------------------------
///
/// 1. Create a file named `secrets.cs` in the same directory as `Program.cs`.
/// 2. Define a public static class `Secrets` with static readonly fields containing
///    your API credentials and default address values.
///
/// Example of `secrets.cs`:
///
/// public static class Secrets
/// {
///     public static readonly string ApiKey = "your_api_key";
///     public static readonly string Secret = "your_secret";
///     public static readonly string CustomerNumber = "your_customer_number";
///     public static readonly string Endpoint = "https://ct.soa-gw.canadapost.ca/rs/ship/price";
///     public static readonly string Street = "your_street";
///     public static readonly string City = "your_city";
///     public static readonly string Province = "your_province";
///     public static readonly string PostalCode = "your_postal_code";
/// }
///
/// 3. The program uses the values from `secrets.cs` to populate the initial
///    configuration file located at:
///    $HOME/.config/gopostal/gopostal.conf
///
/// -------------------- Add `secrets.cs` to `.gitignore` ---------------------
///
/// Make sure that `secrets.cs` is added to your `.gitignore` file to prevent it
/// from being tracked in version control. This ensures that sensitive API keys
/// and personal data are not exposed.
///
/// Add the following line to `.gitignore`:
///
/// secrets.cs
///
/// ============================================================================
/// </summary>

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json.Serialization.Metadata;
using Newtonsoft.Json;

class Program
{
    // private static readonly string ConfigFilePath = Path.Combine(AppContext.BaseDirectory, "config", "appsettings.json");
    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("HOME environment variable is not set"),
        ".config", "gopostal", "gopostal.conf");

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    static async Task Main(string[] args)
    {
        if (args.Length == 0 || args.Length < 10)
        {
            Console.WriteLine("Usage: -w <width> -l <length> -h <height> -m <weight> -t '<to address>' [-f '<from address>']");
            return;
        }

        // Parse the command line arguments
        if (!double.TryParse(GetArgValue(args, "-w"), out var width))
        {
            Console.WriteLine("Invalid width specified.");
            return;
        }
        if (!double.TryParse(GetArgValue(args, "-l"), out var length))
        {
            Console.WriteLine("Invalid length specified.");
            return;
        }
        if (!double.TryParse(GetArgValue(args, "-h"), out var height))
        {
            Console.WriteLine("Invalid height specified.");
            return;
        }
        if (!double.TryParse(GetArgValue(args, "-m"), out var weight))
        {
            Console.WriteLine("Invalid weight specified.");
            return;
        }
        weight = weight / 1000;  // Convert grams to kilograms, if necessary

        // Parse the to address argument
        var toAddress = GetArgValue(args, "-t");
        if (string.IsNullOrEmpty(toAddress))
        {
            Console.WriteLine("To address is missing.");
            return;
        }

        // Load configuration
        var config = LoadConfiguration(ConfigFilePath);
        var canadaPostConfig = config["CanadaPost"] ?? throw new InvalidOperationException("CanadaPost section is missing in the configuration.");

        var apiKey = canadaPostConfig["ApiKey"]?.ToString() ?? throw new InvalidOperationException("ApiKey is missing in the configuration.");
        var secret = canadaPostConfig["Secret"]?.ToString() ?? throw new InvalidOperationException("Secret is missing in the configuration.");
        var customerNumber = canadaPostConfig["CustomerNumber"]?.ToString() ?? throw new InvalidOperationException("CustomerNumber is missing in the configuration.");
        var endpoint = canadaPostConfig["Endpoint"]?.ToString() ?? throw new InvalidOperationException("Endpoint is missing in the configuration.");

        // If -f is not provided, use default from config
        var fromAddress = GetArgValue(args, "-f");
        if (string.IsNullOrEmpty(fromAddress))
        {
            var defaultFromAddress = canadaPostConfig["DefaultFromAddress"] ?? throw new InvalidOperationException("DefaultFromAddress is missing in the configuration.");
            fromAddress = $"{defaultFromAddress["Street"]}, {defaultFromAddress["City"]}, {defaultFromAddress["Province"]}, {defaultFromAddress["PostalCode"]}";
        }

        // Parse the addresses
        var fromComponents = ParseAddress(fromAddress);
        var toComponents = ParseAddress(toAddress);

        // Normalize addresses with Canada Post API
        var normalizedAddresses = await NormalizeAddress(apiKey, secret, customerNumber, fromComponents, toComponents);

        // Extract postal codes from the normalized addresses
        var fromPostalCode = normalizedAddresses.From.Split(',').Last().Trim();  // Extract last part as postal code
        var toPostalCode = normalizedAddresses.To.Split(',').Last().Trim();      // Extract last part as postal code

        // Detect destination country based on the postal code
        var destinationCountry = DetectCountry(toPostalCode);  // Correctly initialize destinationCountry

        // Call exchange rate API only if destination is in the US
        decimal exchangeRate = 1.0m;  // Default to 1.0 for Canada
        if (destinationCountry == "US")
        {
            exchangeRate = await GetExchangeRate();
            Console.WriteLine($"\nExchange Rate (CAD to USD): {exchangeRate}");
        }

        // Calculate shipping costs
        var xmlResponse = await GetShippingCosts(endpoint, apiKey, secret, customerNumber, fromPostalCode, toPostalCode, weight, length, width, height, destinationCountry);

        // Before displaying the rate response table, print the "from" and "to" addresses
        Console.WriteLine("\n*** CANADA POST Delivery Options and Costs for Canadian and US Shipping Addresses ***");
        Console.WriteLine($"\nFrom Address: {normalizedAddresses.From}");
        Console.WriteLine($"To Address:   {normalizedAddresses.To}");

        // Then proceed to display the shipping options
        DisplayRateResponse(xmlResponse, exchangeRate, destinationCountry);
    }

    private static string GetArgValue(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return (index != -1 && args.Length > index + 1) ? args[index + 1] : string.Empty;
    }

    private static JObject LoadConfiguration(string filePath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (directoryPath == null)
            {
                throw new InvalidOperationException("Directory path is null.");
            }

            if (!Directory.Exists(directoryPath))
            {
                // Create the directory if it doesn't exist
                Directory.CreateDirectory(directoryPath);
            }

            if (!File.Exists(filePath))
            {
                // Pre-populated JSON content using the Secrets class
                var defaultConfig = $@"
            {{
              ""CanadaPost"": {{
                ""ApiKey"": ""{Secrets.ApiKey}"",
                ""Secret"": ""{Secrets.Secret}"",
                ""CustomerNumber"": ""{Secrets.CustomerNumber}"",
                ""Endpoint"": ""{Secrets.Endpoint}"",
                ""DefaultFromAddress"": {{
                  ""Street"": ""{Secrets.Street}"",
                  ""City"": ""{Secrets.City}"",
                  ""Province"": ""{Secrets.Province}"",
                  ""PostalCode"": ""{Secrets.PostalCode}""
                }}
              }}
            }}";

                // Write the default configuration to the file
                File.WriteAllText(filePath, defaultConfig);
            }

            var json = File.ReadAllText(filePath);
            return JObject.Parse(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            throw;
        }
    }

    private static async Task<(string From, string To)> NormalizeAddress(string apiKey, string secret, string customerNumber,
        (string Street, string City, string Province, string PostalCode) fromComponents,
        (string Street, string City, string Province, string PostalCode) toComponents)
    {
        // Simulate an asynchronous operation
        await Task.Delay(100); // Simulate a delay for async operation

        // For now, simply return the parsed addresses as the "normalized" values
        var normalizedFrom = $"{fromComponents.Street}, {fromComponents.City}, {fromComponents.Province}, {fromComponents.PostalCode}";
        var normalizedTo = $"{toComponents.Street}, {toComponents.City}, {toComponents.Province}, {toComponents.PostalCode}";

        return (normalizedFrom, normalizedTo);
    }

    private static async Task<decimal> GetExchangeRate()
    {
        using var client = new HttpClient();
        var response = await client.GetStringAsync("https://api.exchangerate-api.com/v4/latest/CAD");

        // Use Newtonsoft.Json for deserialization
        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
        if (data == null || !data.ContainsKey("rates"))
        {
            throw new InvalidOperationException("Exchange rate data is missing or invalid.");
        }

        var rates = data["rates"] as JObject;
        if (rates == null || !rates.TryGetValue("USD", out var usdRate))
        {
            throw new InvalidOperationException("USD rate is missing in the exchange rate data.");
        }

        return usdRate.Value<decimal>();
    }

    private static async Task<string> GetShippingCosts(string apiUrl, string apiKey, string password, string customerNumber,
        string fromPostalCode, string toPostalCode, double weightInKg, double length, double width, double height, string countryCode = "CA")
    {
        try
        {
            string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{password}"));
            var httpClient = new HttpClient();

            // Set HTTP headers on the request message (Authorization only)
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.cpc.ship.rate-v4+xml");

            // Construct XML body for Canada Post rate request
            var xmlRequestBody = $@"
        <mailing-scenario xmlns='http://www.canadapost.ca/ws/ship/rate-v4'>
            <customer-number>{customerNumber}</customer-number>
            <parcel-characteristics>
                <weight>{weightInKg}</weight>
                <dimensions>
                    <length>{length}</length>
                    <width>{width}</width>
                    <height>{height}</height>
                </dimensions>
            </parcel-characteristics>
            <origin-postal-code>{fromPostalCode}</origin-postal-code>
            <destination>
                {(countryCode == "CA" ? $@"
                    <domestic>
                        <postal-code>{toPostalCode}</postal-code>
                    </domestic>"
                        : $@"
                    <united-states>
                        <zip-code>{toPostalCode}</zip-code>
                    </united-states>")}
            </destination>
        </mailing-scenario>";

#if DEBUG
            var doc = XDocument.Parse(xmlRequestBody);
            Console.WriteLine("Full API Request (Formatted):");
            Console.WriteLine(doc.ToString());  // This will pretty print the XML with indentation
            Console.WriteLine("\n\n");
#endif

            // Create StringContent with the XML request body and set the Content-Type header here
            var content = new StringContent(xmlRequestBody, Encoding.UTF8, "application/vnd.cpc.ship.rate-v4+xml");

            // Send the request to Canada Post API
            var response = await httpClient.PostAsync(apiUrl, content);

            // Handle response
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                return result;  // Return the raw XML response
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                string errorResult = await response.Content.ReadAsStringAsync();
                Console.WriteLine(errorResult);
                return string.Empty;  // Return an empty string if there's an error
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred: {ex.Message}");
            return string.Empty;  // Return an empty string if an exception occurs
        }
    }

    private static void DisplayRateResponse(string xmlResponse, decimal exchangeRate, string destinationCountry)
    {
        if (string.IsNullOrEmpty(xmlResponse))
        {
            Console.WriteLine("No response from the Canada Post API.");
            return;
        }

#if DEBUG
    try
    {
        var doc = XDocument.Parse(xmlResponse);
        Console.WriteLine("Full API Response (Formatted):");
        Console.WriteLine(doc.ToString());  // This will pretty print the XML with indentation
        Console.WriteLine("\n\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to pretty print XML: {ex.Message}");
        Console.WriteLine("Raw API Response:");
        Console.WriteLine(xmlResponse);  // Fallback to raw if formatting fails
    }
#endif

        // Load the XML response
        var xdoc = XDocument.Parse(xmlResponse);

        // Extract price-quote elements
        var quotes = xdoc.Descendants("{http://www.canadapost.ca/ws/ship/rate-v4}price-quote")
            .Select(quote => new
            {
                ServiceName = quote.Element("{http://www.canadapost.ca/ws/ship/rate-v4}service-name")?.Value,
                DueAmountCAD = decimal.Parse(quote.Element("{http://www.canadapost.ca/ws/ship/rate-v4}price-details")
                                                .Element("{http://www.canadapost.ca/ws/ship/rate-v4}due").Value) * 1.13m, // Add HST
                DeliveryDays = quote.Element("{http://www.canadapost.ca/ws/ship/rate-v4}service-standard")
                                    .Element("{http://www.canadapost.ca/ws/ship/rate-v4}expected-transit-time")?.Value ?? "n/a",
                DeliveryDate = quote.Element("{http://www.canadapost.ca/ws/ship/rate-v4}service-standard")
                                    .Element("{http://www.canadapost.ca/ws/ship/rate-v4}expected-delivery-date")?.Value ?? "n/a",
                LiabilityCoverage = "" // Blank for now
            })
            .ToList();

        // Sort the quotes by DueAmountCAD
        var sortedQuotes = quotes.OrderBy(q => q.DueAmountCAD).ToList();

        // Output header
     
        Console.WriteLine("\nService Name           |   Cost (CAD) |   Cost (USD) | Delivery Days |  Delivery Date  | Liability Coverage");
        Console.WriteLine(new string('-', 110));  // Underline the header with proper spacing


        foreach (var quote in sortedQuotes)
        {
            // Calculate cost in USD if applicable
            var costInUsd = destinationCountry == "CA" ? "n/a" : Math.Round(quote.DueAmountCAD * exchangeRate, 2).ToString("0.00");

            // Adjust delivery days to center it within 7 characters
            var deliveryDays = quote.DeliveryDays.PadLeft((7 + quote.DeliveryDays.Length) / 2).PadRight(7);

            // Adjust delivery date to have uniform spacing
            var deliveryDate = " " + quote.DeliveryDate.PadRight(12);  // Add one leading space for extra padding

            // Output the details in a formatted table with aligned decimal points
            if (destinationCountry == "CA")
            {
                Console.WriteLine($"{quote.ServiceName,-22} | {quote.DueAmountCAD,12:0.00} | {"n/a",12} |    {deliveryDays}    |  {deliveryDate}  | {quote.LiabilityCoverage}");
            }
            else
            {
                Console.WriteLine($"{quote.ServiceName,-22} | {quote.DueAmountCAD,12:0.00} | {costInUsd,12} |    {deliveryDays}    |  {deliveryDate}  | {quote.LiabilityCoverage}");
            }
        }
    }


    private static (string Street, string City, string Region, string PostalCode) ParseAddress(string address)
    {
        // Updated regex to handle no comma between state/province and postal code, including optional hyphen in postal codes
        var regex = new Regex(@"^(?<Street>[^,]+),\s*(?<City>[^,]+),\s*(?<Region>[A-Z]{2}),?\s*(?<PostalCode>\d{5}(-\d{4})?|\w{3}\s?\w{3})$");
        var match = regex.Match(address);

        // If the format doesn't match, throw a detailed error message
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid address format: '{address}'. Expected format: 'Street, City, Province/State, PostalCode'.");
        }

        // Extract the components from the regex match groups
        var street = match.Groups["Street"].Value;
        var city = match.Groups["City"].Value;
        var region = match.Groups["Region"].Value; // Province (Canada) or State (US)
        var postalCode = match.Groups["PostalCode"].Value.Trim().Replace(" ", "");

        return (street, city, region, postalCode);
    }
    
    private static string DetectCountry(string postalCode)
    {
        // Simple detection: US if numeric, Canada if alphanumeric
        if (System.Text.RegularExpressions.Regex.IsMatch(postalCode.Trim(), @"^\d{5}(-\d{4})?$"))
        {
            return "US";
        }
        return "CA";  // Assume Canada if it's not in US format
    }
}