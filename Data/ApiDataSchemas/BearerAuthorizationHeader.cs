using Microsoft.AspNetCore.Mvc;

namespace SerbleAPI.Data.ApiDataSchemas; 

/// <summary>
/// An object that gets the Basic Authorization header from a request
/// </summary>
public class BearerAuthorizationHeader {
        
    [FromHeader]
    public string Authorization { get; set; }  // DO NOT SET TO PRIVATE IT BREAKS THE [FromHeader] ATTRIBUTE

    /// <summary>
    /// Get the specified bearer token from the header
    /// </summary>
    /// <returns>The token as specified in the header</returns>
    public string GetToken() {
        return Authorization.Split(' ')[1];
    }
    
    /// <summary>
    /// Checks if the header is valid
    /// </summary>
    /// <returns>Whether or not the header is valid</returns>
    public bool IsValid() {
        if (Authorization == null!) {
            return false;
        }
        
        string[] data = Authorization.Split(' ');
        if (data.Length != 2) {
            return false;
        }
        
        if (data[0] != "Bearer") {
            return false;
        }
        
        return true;
    }
        
    /// <summary>
    /// Get the specified header type from the header
    /// </summary>
    /// <returns></returns>
    public string GetAuthType() => Authorization.Split(' ')[0];

    /// <summary>
    /// Converts object to string
    /// </summary>
    /// <returns>The raw header data</returns>
    public override string ToString() => Authorization;
}