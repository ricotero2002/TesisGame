[System.Serializable]
public class TokensResponse
{
    public string refresh;
    public string access; // O "access" según lo que devuelva realmente tu servidor
}

[System.Serializable]
public class LoginResponse
{
    public string username;
    public string email;
    public TokensResponse tokens;
    public string detail;
}

[System.Serializable]
public class LoginRequestData
{
    public string username;
    public string password;

    public LoginRequestData(string username, string password)
    {
        this.username = username;
        this.password = password;
    }
}


