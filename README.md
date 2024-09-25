# GoPostal Shipping Calculator

## Overview

**GoPostal** is a .NET console application that calculates shipping costs using the Canada Post API. It takes package dimensions, weight, and destination address from the command line. Optionally, the user can provide a custom "from" address, otherwise a default "from" address is used from the configuration file.

## Features

* Canada Post shipping rates calculation
* Handles Canadian and US addresses
* Option to provide custom "from" address
* Automatic exchange rate calculation for US destinations

## Usage

```
bash
Copy code
gopostal -w <width> -l <length> -h <height> -m <weight> -t '<to address>' [-f '<from address>']
```

### Example

```
bash
Copy code
gopostal -w 10 -l 20 -h 15 -m 500 -t '123 Main St, Anytown, ON, A1A1A1' -f '456 Elm St, Othertown, BC, B2B2B2'
```

### Command Line Arguments

* `-w`: Width of the package (in cm)
* `-l`: Length of the package (in cm)
* `-h`: Height of the package (in cm)
* `-m`: Weight of the package (in grams)
* `-t`: To address (mandatory)
* `-f`: From address (optional, defaults to the configuration file)

## Configuration

The application uses a configuration file located at:\
**`~/.config/gopostal/gopostal.conf`**

If the configuration file doesn't exist, it is automatically created with the following default JSON structure:

```
json
Copy code
{
  "CanadaPost": {
    "ApiKey": "your_api_key",
    "Secret": "your_secret",
    "CustomerNumber": "your_customer_number",
    "Endpoint": "https://ct.soa-gw.canadapost.ca/rs/ship/price",
    "DefaultFromAddress": {
      "Street": "your_street",
      "City": "your_city",
      "Province": "your_province",
      "PostalCode": "your_postal_code"
    }
  }
}
```

### Configuration Fields

* **ApiKey**: Your Canada Post API key.
* **Secret**: Your Canada Post API secret.
* **CustomerNumber**: Your Canada Post customer number.
* **Endpoint**: The Canada Post API endpoint.
* **DefaultFromAddress**: A default "from" address to be used if not provided via command line.

## Dependencies

* **System.Net.Http**: For making HTTP requests to the Canada Post API.
* **Newtonsoft.Json**: For JSON parsing and serialization.
* **System.Xml.Linq**: For XML manipulation to parse API responses.

Make sure you have these dependencies installed and referenced in your project.

## Output Example

```
bash
Copy code
--- CANADA POST Options and Costs for Canadian and US Shipping Addresses ---
Service Name           |   Cost (CAD) |   Cost (USD) | Delivery Days |  Delivery Date  | Liability Coverage
------------------------------------------------------------------------------------------------------------
Expedited Parcel USA   |        22.69 |        16.86 |    5    |  2024-10-03    | n/a
Small Packet USA Air   |        11.52 |         8.56 |   n/a   |  n/a           | n/a
Tracked Packet - USA   |        11.74 |         8.72 |    6    |  2024-10-04    | n/a
Xpresspost USA         |        31.21 |        23.19 |    4    |  2024-10-02    | n/a
```

For Canadian addresses, the **Cost (USD)** column will display `n/a`.

## Development

To run this project, clone the repository and follow the steps below:

1. **Clone the repository**:

   ```
   bash
   Copy code
   git clone https://github.com/your-username/gopostal.git
   cd gopostal
   ```

2. **Restore NuGet packages**:

   ```
   bash
   Copy code
   dotnet restore
   ```

3. **Run the application**:

   ```
   bash
   Copy code
   dotnet run -- -w 10 -l 20 -h 15 -m 500 -t '123 Main St, Anytown, ON, A1A1A1'
   ```

## License

This project is licensed under the MIT License. See the LICENSE file for details.
