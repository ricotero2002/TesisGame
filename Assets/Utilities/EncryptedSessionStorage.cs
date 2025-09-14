
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class EncryptedSessionStorage
{
    private static readonly string filePath = Path.Combine(Application.persistentDataPath, "sessionData.bin");
    // Clave de encriptación (debe ser de 16, 24 o 32 bytes para AES)
    // En un proyecto real, esta clave no se debe hardcodear y se debe almacenar de forma segura.
    private static readonly byte[] encryptionKey = Encoding.UTF8.GetBytes("EstaClave16Bytes"); // Ejemplo: 16 bytes

    public static void SaveSession(UserConecctionData data)
    {
        try
        {
            // Serializar el objeto a bytes
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, data);
                byte[] rawData = ms.ToArray();
                // Encriptar los datos
                byte[] encryptedData = Encrypt(rawData, encryptionKey);
                // Guardar en archivo
                File.WriteAllBytes(filePath, encryptedData);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error guardando sesión: " + ex.Message);
        }
    }

    public static UserConecctionData LoadSession()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            byte[] encryptedData = File.ReadAllBytes(filePath);
            byte[] rawData = Decrypt(encryptedData, encryptionKey);
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream(rawData))
            {
                return (UserConecctionData)bf.Deserialize(ms);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error cargando sesión: " + ex.Message);
            return null;
        }
    }

    public static void ClearSession()
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                Debug.Log("Sesión borrada correctamente.");
            }
            catch (Exception ex)
            {
                Debug.LogError("Error al borrar la sesión: " + ex.Message);
            }
        }
        else
        {
            Debug.Log("No existe ningún archivo de sesión para borrar.");
        }
    }


    private static byte[] Encrypt(byte[] data, byte[] key)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.GenerateIV(); // Generamos un vector de inicialización aleatorio
            using (MemoryStream ms = new MemoryStream())
            {
                // Escribimos el IV al inicio del archivo, porque lo necesitamos para desencriptar
                ms.Write(aes.IV, 0, aes.IV.Length);
                using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();
                }
                return ms.ToArray();
            }
        }
    }

    private static byte[] Decrypt(byte[] data, byte[] key)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            // El IV está almacenado en los primeros 16 bytes de los datos
            byte[] iv = new byte[aes.BlockSize / 8];
            Array.Copy(data, 0, iv, 0, iv.Length);
            aes.IV = iv;
            using (MemoryStream ms = new MemoryStream())
            {
                // Usamos CryptoStream para desencriptar desde data, omitiendo el IV
                using (CryptoStream cs = new CryptoStream(
                    new MemoryStream(data, iv.Length, data.Length - iv.Length),
                    aes.CreateDecryptor(),
                    CryptoStreamMode.Read))
                {
                    cs.CopyTo(ms);
                }
                return ms.ToArray();
            }
        }
    }
}
