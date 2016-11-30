using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using SocketIO;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.OpenSsl;
using System.Runtime.Serialization;
using System.Text;

public class NetworkManager : MonoBehaviour {

    public static NetworkManager instance;
    public Canvas canvas;
    public SocketIOComponent socket;
    public InputField playerNameInput;
    public GameObject player;
    public AsymmetricCipherKeyPair clientKeyPair;
    public int nonce;
    public bool canLogin;
    public bool logged;
    public AudioSource backgroundMusic;

    void OnApplicationFocus(bool focus)
    {
        if (focus && logged)
            StartCoroutine(LockCursor());
    }

    void OnApplicationPaused(bool focus) {
        print("Is paused");
    }

    IEnumerator LockCursor()
    {
        yield return new WaitForSeconds(0.1f);
        Cursor.lockState = UnityEngine.CursorLockMode.Locked;
    }

    void Awake() {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    // Use this for initialization
    void Start()
    {
        // subscribe to all the various websocket events
        socket.On("enemies", OnEnemies);
        socket.On("other player connected", OnOtherPlayerConnected);
        socket.On("play", OnPlay);
        socket.On("player move", OnPlayerMove);
        socket.On("player turn", OnPlayerTurn);
        socket.On("player shoot", OnPlayerShoot);
        socket.On("health", OnHealth);
        socket.On("other player disconnected", OnOtherPlayerDisconnect);
        socket.On("can login", OnCanLogin);
    }
    

    public void JoinGame() {
        StartCoroutine(ConnectToServer());
    }

    #region Commands
    IEnumerator ConnectToServer() {
        canLogin = false;
        logged = false;
        string playerName = playerNameInput.text;
        PlayerAskJSON playerAskJSON = new PlayerAskJSON(playerName);
        string data = JsonUtility.ToJson(playerAskJSON);
        socket.Emit("check username", new JSONObject(data));
        yield return new WaitForSeconds(1f);
        if (canLogin)
        {
            //Crypto
            GenerateKeyPair();
            string cipheredStr = encryptWithServerPublic(getPublicClientKey());

            System.Random rnd = new System.Random();
            nonce = rnd.Next(0, 9999);
            string nonceStr = nonce.ToString(); //mod 10000
            string encrypteduser = encryptWithUserPrivate(nonceStr, clientKeyPair);
            string encryptedNonce = encryptWithServerPublic(encrypteduser);
            //Crypto
            yield return new WaitForSeconds(0.5f);

            socket.Emit("player connect");

            yield return new WaitForSeconds(1f);


            List<SpawnPoint> playerSpawnPoints = GetComponent<PlayerSpawner>().playerSpawnPoints;
            List<SpawnPoint> enemySpawnPoints = GetComponent<EnemySpawner>().enemySpawnPoints;
            PlayerJSON playerJSON = new PlayerJSON(playerName, playerSpawnPoints, enemySpawnPoints, cipheredStr, encryptedNonce);
            data = JsonUtility.ToJson(playerJSON);
            socket.Emit("play", new JSONObject(data));
            canvas.gameObject.SetActive(false);
            Cursor.lockState = UnityEngine.CursorLockMode.Locked;
            Cursor.visible = false;
            backgroundMusic.enabled = true;
            logged = true;
        }
    }

    public void CommandMove(Vector3 vec3) {
        //string data = JsonUtility.ToJson(new PositionJSON(playerNameInput.text, vec3));
        nonce = (nonce + 1)%10000;
        string data = JsonUtility.ToJson(new MasterJSON(new PositionJSON(playerNameInput.text, vec3), clientKeyPair, nonce.ToString()));
        socket.Emit("player move", new JSONObject(data));
    }

    public void CommandTurn(Quaternion quat) {
        string data = JsonUtility.ToJson(new RotationJSON(quat));
        socket.Emit("player turn", new JSONObject(data));
    }

    public void CommandShoot() {
        print("Shoot");
        socket.Emit("player shoot");
    }

    public void CommandHealthChanged(GameObject playerFrom, GameObject playerTo, int healthChange, bool isEnemy) {
        print("health change cmd");
        HealthChangeJSON healthChangeJSON = new HealthChangeJSON(playerTo.name, healthChange, playerFrom.name, isEnemy);
        socket.Emit("health", new JSONObject(JsonUtility.ToJson(healthChangeJSON)));
    }

    public void CommandRestoreHealth() {
        print("restoring health");
        socket.Emit("restoreHealth");
    }

    #endregion

    #region Listening
    void OnCanLogin(SocketIO.SocketIOEvent socketIOEvent)
    {
        PlayerReplyJSON response = PlayerReplyJSON.CreateFromJSON(socketIOEvent.data.ToString());
        canLogin = response.reply.Equals("yes") ? true : false;
    }
    void OnEnemies(SocketIO.SocketIOEvent socketIOEvent)
    {
        EnemiesJSON enemiesJSON = EnemiesJSON.CreateFromJSON(socketIOEvent.data.ToString());
        EnemySpawner es = GetComponent<EnemySpawner>();
        es.SpawnEnemies(enemiesJSON);
    }
    void OnOtherPlayerConnected(SocketIO.SocketIOEvent socketIOEvent)
    {
        print("Someone else joined");
        string data = socketIOEvent.data.ToString();
        UserJSON userJSON = UserJSON.CreateFromJSON(data);
        Vector3 position = new Vector3(userJSON.position[0], userJSON.position[1], userJSON.position[2]);
        Quaternion rotation = Quaternion.Euler(userJSON.rotation[0], userJSON.rotation[1], userJSON.rotation[2]);
        GameObject o = GameObject.Find(userJSON.name) as GameObject;
        if (o != null) {
            return;
        }
        GameObject p = Instantiate(player, position, rotation) as GameObject;
        //We are setting up their other fields name and if they are local
        PlayerController pc = p.GetComponent<PlayerController>();
        Transform t = p.transform.Find("HealthBarCanvas");
        Transform t1 = t.transform.Find("PlayerName");
        Text playerName = t1.GetComponent<Text>();
        playerName.text = userJSON.name;
        pc.isLocalPlayer = false;
        p.name = userJSON.name;
        // we also need to set the health
        Health h = p.GetComponent<Health>();
        h.currentHealth = userJSON.health;
        h.OnChangeHealth();
    }
    void OnPlay(SocketIO.SocketIOEvent socketIOEvent)
    {
        print("You joined");
        string data = socketIOEvent.data.ToString();
        UserJSON currentuserJSON = UserJSON.CreateFromJSON(data);
        Vector3 position = new Vector3(currentuserJSON.position[0], currentuserJSON.position[1], currentuserJSON.position[2]);
        Quaternion rotation = Quaternion.Euler(currentuserJSON.rotation[0], currentuserJSON.rotation[1], currentuserJSON.rotation[2]);
        GameObject p = Instantiate(player, position, rotation) as GameObject;
        PlayerController pc = p.GetComponent<PlayerController>();
        Transform t = p.transform.Find("HealthBarCanvas");
        Transform t1 = t.transform.Find("PlayerName");
        Text playerName = t1.GetComponent<Text>();
        playerName.text = currentuserJSON.name;
        pc.isLocalPlayer = true;
        p.name = currentuserJSON.name;
    }
    void OnPlayerMove(SocketIO.SocketIOEvent socketIOEvent)
    {
        string data = socketIOEvent.data.ToString();
        UserJSON userJSON = UserJSON.CreateFromJSON(data);
        Vector3 position = new Vector3(userJSON.position[0], userJSON.position[1], userJSON.position[2]);
        if (userJSON.name == playerNameInput.text) {
            return;
        }
        GameObject p = GameObject.Find(userJSON.name) as GameObject;
        if (p != null) {
            p.transform.position = position;
        }
    }
    void OnPlayerTurn(SocketIO.SocketIOEvent socketIOEvent)
    {
        string data = socketIOEvent.data.ToString();
        UserJSON userJSON = UserJSON.CreateFromJSON(data);
        Quaternion rotation = Quaternion.Euler(userJSON.rotation[0], userJSON.rotation[1], userJSON.rotation[2]);
        if (userJSON.name == playerNameInput.text)
        {
            return;
        }
        GameObject p = GameObject.Find(userJSON.name) as GameObject;
        if (p != null)
        {
            p.transform.rotation = rotation;
        }
    }
    void OnPlayerShoot(SocketIO.SocketIOEvent socketIOEvent)
    {
        string data = socketIOEvent.data.ToString();
        ShootJSON shootJSON = ShootJSON.CreateFromJSON(data);
        //find the gameObject
        GameObject p = GameObject.Find(shootJSON.name);
        //instantiate the bullet etc from the player script
        PlayerController pc = p.GetComponent<PlayerController>();
        pc.CmdFire();
    }
    void OnHealth(SocketIO.SocketIOEvent socketIOEvent)
    {
        print("Changing the health");
        //get the name of the player whose health change
        string data = socketIOEvent.data.ToString();
        UserHealthJSON userHealthJSON = UserHealthJSON.CreateFromJSON(data);
        GameObject p = GameObject.Find(userHealthJSON.name);
        Health h = p.GetComponent<Health>();
        h.currentHealth = userHealthJSON.health;
        h.OnChangeHealth();
    }
    void OnOtherPlayerDisconnect(SocketIO.SocketIOEvent socketIOEvent)
    {
        print("user disconnected");
        string data = socketIOEvent.data.ToString();
        UserJSON userJSON = UserJSON.CreateFromJSON(data);
        Destroy(GameObject.Find(userJSON.name));
    }

    #endregion

    #region JSONMessageCLasses

    //Master JSON class
    [Serializable]
    public class MasterJSON {
        public string json;
        public string signature;
        public MasterJSON(JsonClass data, AsymmetricCipherKeyPair clientKeyPair, string nonce) {
            json = JsonUtility.ToJson(data);
            string md5 = CalculateMD5Hash(json + nonce);
            string ciphered = encryptWithUserPrivate(md5, clientKeyPair);
            //string ciphered = encryptWithUserPrivate("hello", clientKeyPair);
            //string plain = decryptWithUserPublic(ciphered, clientKeyPair);
            signature = ciphered;
        }
    }

    [Serializable]
    public class PlayerAskJSON
    {
        public string username;
        public PlayerAskJSON(string _username)
        {
            username = _username;
        }
    }
    [Serializable]
    public class PlayerReplyJSON
    {
        public string reply;
        public PlayerReplyJSON(string _reply)
        {
            reply = _reply;
        }
        public static PlayerReplyJSON CreateFromJSON(string data)
        {
            return JsonUtility.FromJson<PlayerReplyJSON>(data);
        }
    }

    [Serializable]
    public abstract class JsonClass{

    }
    

    [Serializable]
    public class PlayerJSON : JsonClass{
        public string name;
        public string publicKey;
        public string nonce;
        public List<PointJSON> playerSpawnPoints;
        public List<PointJSON> enemySpawnPoints;

        public PlayerJSON(string _name, List<SpawnPoint> _playerSpawnPoints, List<SpawnPoint> _enemySpawnPoints, string _key, string _nonce) {
            playerSpawnPoints = new List<PointJSON>();
            enemySpawnPoints = new List<PointJSON>();
            name = _name;
            publicKey = _key;
            nonce = _nonce;

            foreach (SpawnPoint playerSpawnPoint in _playerSpawnPoints) {
                PointJSON pointJSON = new PointJSON(playerSpawnPoint);
                playerSpawnPoints.Add(pointJSON);
            }

            foreach (SpawnPoint enemySpawnPoint in _enemySpawnPoints) {
                PointJSON pointJSON = new PointJSON(enemySpawnPoint);
                enemySpawnPoints.Add(pointJSON);
            }
        }
    }

    [Serializable]
    public class PointJSON : JsonClass
    {
        public float[] position;
        public float[] rotation;

        public PointJSON(SpawnPoint spawnPoint) {
            position = new float[] {
                spawnPoint.transform.position.x,
                spawnPoint.transform.position.y,
                spawnPoint.transform.position.z
            };

            rotation = new float[] {
                spawnPoint.transform.eulerAngles.x,
                spawnPoint.transform.eulerAngles.y,
                spawnPoint.transform.eulerAngles.z
            };
        }
    }

    [Serializable]
    public class PositionJSON : JsonClass
    {
        public string name;
        public float[] position;

        public PositionJSON(string _name, Vector3 _position)
        {
            name = _name;
            position = new float[] { _position.x, _position.y, _position.z };
        }
    }

    [Serializable]
    public class RotationJSON : JsonClass
    {
        public float[] rotation;

        public RotationJSON(Quaternion _rotation) {
            rotation = new float[] { _rotation.eulerAngles.x, _rotation.eulerAngles.y, _rotation.eulerAngles.z };
        }
    }

    [Serializable]
    public class UserJSON : JsonClass
    {
        public string name;
        public float[] position;
        public float[] rotation;
        public int health;

        public static UserJSON CreateFromJSON(string data) {
            return JsonUtility.FromJson<UserJSON>(data);
        }
    }

    [Serializable]
    public class HealthChangeJSON : JsonClass
    {
        public string name;
        public int healthChange;
        public string from;
        public bool isEnemy;

        public HealthChangeJSON(string _name, int _healthChange, string _from, bool _isEnemy) {
            name = _name;
            healthChange = _healthChange;
            from = _from;
            isEnemy = _isEnemy;
        }
    }

    [Serializable]
    public class EnemiesJSON : JsonClass
    {
        public List<UserJSON> enemies;

        public static EnemiesJSON CreateFromJSON(string data) {
            return JsonUtility.FromJson<EnemiesJSON>(data);        }
    }

    [Serializable]
    public class ShootJSON : JsonClass
    {
        public string name;

        public static ShootJSON CreateFromJSON(string data) {
            return JsonUtility.FromJson<ShootJSON>(data);
        }
    }

    [Serializable]
    public class UserHealthJSON : JsonClass
    {
        public string name;
        public int health;

        public static UserHealthJSON CreateFromJSON(string data) {
            return JsonUtility.FromJson<UserHealthJSON>(data);
        }
    }
    #endregion

    #region Crypto
    private const int RsaKeySize = 1024;
    private void GenerateKeyPair()
    {
        CryptoApiRandomGenerator randomGenerator = new CryptoApiRandomGenerator();
        SecureRandom secureRandom = new SecureRandom(randomGenerator);
        var keyGenerationParameters = new KeyGenerationParameters(secureRandom, RsaKeySize);

        var keyPairGenerator = new RsaKeyPairGenerator();
        keyPairGenerator.Init(keyGenerationParameters);
        clientKeyPair = keyPairGenerator.GenerateKeyPair();
        //return keyPair;
    }
    private string getPublicClientKey() {
        System.IO.TextWriter textWriter = new System.IO.StringWriter();
        PemWriter pemWriter = new PemWriter(textWriter);
        pemWriter.WriteObject(clientKeyPair.Public);
        pemWriter.Writer.Flush();

        string publicKey = textWriter.ToString();
        return publicKey;
    }
    public static string encryptWithServerPublic(string content) {
        string publicKeyPath = Application.dataPath + "/public_key.txt";
        var bytesToEncrypt = System.Text.Encoding.UTF8.GetBytes(content);
        AsymmetricKeyParameter keyPair;
        using (var reader = System.IO.File.OpenText(publicKeyPath))
            keyPair = (AsymmetricKeyParameter)new Org.BouncyCastle.OpenSsl.PemReader(reader).ReadObject();

        //var engine = new Org.BouncyCastle.Crypto.Encodings.Pkcs1Encoding(new Org.BouncyCastle.Crypto.Engines.RsaEngine());
        var engine = new Org.BouncyCastle.Crypto.Engines.RsaEngine();
        engine.Init(true, keyPair);

        var encrypted = engine.ProcessBlock(bytesToEncrypt, 0, bytesToEncrypt.Length);

        var cryptMessage = Convert.ToBase64String(encrypted);
        
        return cryptMessage;
    }
    private static string encryptWithUserPrivate(string content, AsymmetricCipherKeyPair clientKeyPair) {
        var bytesToEncrypt = System.Text.Encoding.UTF8.GetBytes(content);
        var engine = new Org.BouncyCastle.Crypto.Engines.RsaEngine();
        engine.Init(true, clientKeyPair.Private);

        var encrypted = engine.ProcessBlock(bytesToEncrypt, 0, bytesToEncrypt.Length);
        var cryptMessage = Convert.ToBase64String(encrypted);

        return cryptMessage;
    }

    private static string decryptWithUserPublic(string content, AsymmetricCipherKeyPair clientKeyPair)
    {

        var bytesToDecrypt = Convert.FromBase64String(content);

        var decryptEngine = new Org.BouncyCastle.Crypto.Engines.RsaEngine();

        decryptEngine.Init(false, clientKeyPair.Public);

        var decrypted = Encoding.UTF8.GetString(decryptEngine.ProcessBlock(bytesToDecrypt, 0, bytesToDecrypt.Length));
        return decrypted;
    }

    private static string CalculateMD5Hash(string input)
    {
        // step 1, calculate MD5 hash from input
        MD5 md5 = System.Security.Cryptography.MD5.Create();
        byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
        byte[] hash = md5.ComputeHash(inputBytes);
        // step 2, convert byte array to hex string
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < hash.Length; i++)
        {
            sb.Append(hash[i].ToString("X2"));
        }
        return sb.ToString();
    }
    #endregion
}
