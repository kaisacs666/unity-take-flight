using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Thirdweb;
using Thirdweb.Unity;
using UnityEngine;
using UnityEngine.Events;

public class BlockchainManager : MonoBehaviour
{
    public UnityEvent<string> OnLoggedIn;

    public IThirdwebWallet Wallet { get; private set; }
    public string Address { get; private set; }

    public static BlockchainManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public async void Login(string authProvider)
    {
        AuthProvider provider = AuthProvider.Google;
        switch (authProvider)
        {
            case "google":
                provider = AuthProvider.Google;
                break;
            case "apple":
                provider = AuthProvider.Apple;
                break;
            case "facebook":
                provider = AuthProvider.Facebook;
                break;
        }

        var connection = new WalletOptions(
            provider: WalletProvider.InAppWallet,
            chainId: 10143,
            inAppWalletOptions: new InAppWalletOptions(authprovider: provider),
            smartWalletOptions: new SmartWalletOptions(sponsorGas: true)
        );

        Wallet = await ThirdwebManager.Instance.ConnectWallet(connection);
        Address = await Wallet.GetAddress();
        Debug.Log($"Login  address  is {Address}");
        OnLoggedIn?.Invoke(Address);
    }

    private async Task<bool> CheckWalletStatus()
    {
        try
        {
            // 检查钱包是否已连接
            if (Wallet == null)
            {
                Debug.LogWarning("钱包未连接");
                return false;
            }

            // 检查钱包地址
            var address = await Wallet.GetAddress();
            if (string.IsNullOrEmpty(address))
            {
                Debug.LogWarning("钱包地址为空");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"检查钱包状态失败: {ex.Message}");
            return false;
        }
    }

    internal async Task SubmitScore(float distanceTravelled)
    {
        try
        {
            Debug.Log($"开始提交分数 {distanceTravelled} 到区块链，地址 {Address}");
            
            // 检查钱包连接状态
            if (Wallet == null)
            {
                Debug.LogError("钱包未连接，请先登录");
                throw new Exception("钱包未连接，请先登录");
            }

            // 检查是否在WebGL平台
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                Debug.Log("检测到WebGL平台，等待页面加载...");
                await Task.Delay(3000); // 增加初始等待时间到3秒
                
                // 检查钱包状态
                if (!await CheckWalletStatus())
                {
                    Debug.LogError("钱包未解锁或未连接，请检查登录状态");
                    throw new Exception("钱包未解锁或未连接，请检查登录状态");
                }
            }

            // 添加重试机制
            int maxRetries = 3;
            int currentRetry = 0;
            bool success = false;
            Exception lastException = null;
            ThirdwebContract contract = null;

            // 获取合约实例
            try {
                contract = await ThirdwebManager.Instance.GetContract(
                    "0x22324302c5c684d6763e63CdBbB41ABbD5dF38D0",
                    10143,
                    "[{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"internalType\":\"address\",\"name\":\"player\",\"type\":\"address\"},{\"indexed\":false,\"internalType\":\"uint256\",\"name\":\"score\",\"type\":\"uint256\"}],\"name\":\"ScoreAdded\",\"type\":\"event\"},{\"inputs\":[{\"internalType\":\"address\",\"name\":\"player\",\"type\":\"address\"}],\"name\":\"getRank\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"rank\",\"type\":\"uint256\"}],\"stateMutability\":\"view\",\"type\":\"function\"},{\"inputs\":[{\"internalType\":\"uint256\",\"name\":\"score\",\"type\":\"uint256\"}],\"name\":\"submitScore\",\"outputs\":[],\"stateMutability\":\"nonpayable\",\"type\":\"function\"}]"
                );
            } catch (Exception ex) {
                Debug.LogError($"获取合约实例失败: {ex.Message}");
                throw new Exception("获取合约实例失败，请检查网络连接");
            }

            while (currentRetry < maxRetries && !success)
            {
                try
                {
                    Debug.Log($"尝试提交分数 (尝试 {currentRetry + 1}/{maxRetries})");
                    
                    // 每次提交前都检查钱包状态
                    if (!await CheckWalletStatus())
                    {
                        // 尝试重新连接钱包
                        var connection = new WalletOptions(
                            provider: WalletProvider.InAppWallet,
                            chainId: 10143,
                            inAppWalletOptions: new InAppWalletOptions(authprovider: AuthProvider.Google),
                            smartWalletOptions: new SmartWalletOptions(sponsorGas: true)
                        );
                        
                        Debug.Log("尝试重新连接钱包...");
                        Wallet = await ThirdwebManager.Instance.ConnectWallet(connection);
                        await Task.Delay(2000);
                        
                        if (!await CheckWalletStatus())
                        {
                            throw new Exception("重新连接钱包失败");
                        }
                    }
                    
                    // 获取交易前的排名
                    int rankBefore = 0;
                    try {
                        rankBefore = await contract.Read<int>("getRank", Address);
                        Debug.Log($"提交前排名: {rankBefore}");
                    } catch (Exception ex) {
                        Debug.LogWarning($"获取提交前排名失败: {ex.Message}");
                    }
                    
                    // 尝试提交分数
                    Debug.Log($"正在发送交易...");
                    var result = await contract.Write(Wallet, "submitScore", 0, (int)distanceTravelled);
                    
                    if (result == null)
                    {
                        throw new Exception("交易返回为空，提交失败");
                    }

                    // 等待交易确认
                    Debug.Log("等待交易确认...");
                    await Task.Delay(5000); // 等待5秒让交易确认
                    
                    // 验证分数是否成功提交
                    int rankAfter = await contract.Read<int>("getRank", Address);
                    Debug.Log($"提交后排名: {rankAfter}");
                    
                    if (rankBefore == rankAfter && rankAfter != 0)
                    {
                        throw new Exception("交易可能未成功，排名未发生变化");
                    }
                    
                    success = true;
                    Debug.Log($"分数提交成功！交易已确认，排名从 {rankBefore} 变为 {rankAfter}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Debug.LogWarning($"提交分数尝试 {currentRetry + 1} 失败: {ex.Message}");
                    
                    if (currentRetry < maxRetries - 1)
                    {
                        // 递增等待时间
                        await Task.Delay(2000 * (currentRetry + 1));
                    }
                }
                currentRetry++;
            }

            if (!success)
            {
                string errorMessage = "提交分数失败，请检查：\n" +
                                    "1. 网络连接是否正常\n" +
                                    "2. 是否已正确登录\n" +
                                    "3. 是否在正确的网络上\n" +
                                    "4. 尝试刷新页面后重试\n" +
                                    $"错误详情: {lastException?.Message}";
                Debug.LogError(errorMessage);
                throw new Exception(errorMessage);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"提交分数时发生错误: {ex.Message}");
            throw;
        }
    }

    internal async Task<int> GetRank()
    {
        var contract = await ThirdwebManager.Instance.GetContract(
            "0x22324302c5c684d6763e63CdBbB41ABbD5dF38D0",
            10143,
            "[{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"internalType\":\"address\",\"name\":\"player\",\"type\":\"address\"},{\"indexed\":false,\"internalType\":\"uint256\",\"name\":\"score\",\"type\":\"uint256\"}],\"name\":\"ScoreAdded\",\"type\":\"event\"},{\"inputs\":[{\"internalType\":\"address\",\"name\":\"player\",\"type\":\"address\"}],\"name\":\"getRank\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"rank\",\"type\":\"uint256\"}],\"stateMutability\":\"view\",\"type\":\"function\"},{\"inputs\":[{\"internalType\":\"uint256\",\"name\":\"score\",\"type\":\"uint256\"}],\"name\":\"submitScore\",\"outputs\":[],\"stateMutability\":\"nonpayable\",\"type\":\"function\"}]"
        );
        var address = await Wallet.GetAddress();
        //var strcontract = await contract.GetContract().GetContractAddress();
        var rank = await contract.Read<int>("getRank", Address);
        Debug.Log($"Rank for address {Address} is {rank}");
        //Debug.Log($"Contract address is {strcontract}");
        return rank;
    }
}
