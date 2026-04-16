using System.Net.Http;
using System.Text;
using WpfApiTester.Helpers;

namespace WpfApiTester;

public class CDdeposit
{
    public async Task<string> GetResponseAsync(string requestXml, string endpoint, bool legacy)
    {
        try
        {
            string responseXml;

            if (legacy)
                responseXml = await GetLegacyResponseAsXmlAsync(requestXml, endpoint);
            else
                responseXml = await GetNewResponse(requestXml, endpoint);

            return responseXml;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error sending SOAP request: " + ex.Message);
            return string.Empty;
        }
    }

    private async Task<string> GetLegacyResponseAsXmlAsync(string requestXml, string endpoint)
    {
        try
        {
            // TODO: implement legacy CD Deposit call
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return string.Empty;
        }
    }

    private async Task<string> GetNewResponse(string SoapXMLRequest, string endpoint)
    {
        HttpClient httpClient = new HttpClient();
        try
        {
            string wrappedRequest = SoapEnvelopeBuilder.WrapInSoapEnvelope(SoapXMLRequest);
            var content = new StringContent(wrappedRequest, Encoding.UTF8, "application/xml");
            var response = await httpClient.PostAsync(endpoint, content);
            var responseXMLContent = await response.Content.ReadAsStringAsync();
            return responseXMLContent;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error reading XML Response: " + ex.Message);
            return string.Empty;
        }
    }
}
