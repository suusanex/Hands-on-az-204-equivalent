# 課題1：redmineの添付ファイル検知・処理呼び出しをAzure Functionsで行う

単発のFunctionsではなく、DurableFunctionsを使用します。この課題の後の処理もまとめてDurableFunctionsで管理する想定です。

## Azure CLI

※記載するコマンドの[]で囲んだ部分は、可変の値。環境や状況に合わせて変更する必要がある。

### インストール

[Azure CLI をインストールする方法](https://docs.microsoft.com/ja-jp/cli/azure/install-azure-cli)

### ログイン

```
az login
```

組織アカウント等で、Azureへのログオンを行う。

### 使うサブスクリプションを特定しておく

```
az account subscription list
```

サブスクリプションの一覧が出るので、使うサブスクリプション名をメモる。この後のコマンド全ての「[yourSubscription]」で使用する。

### 使えるリージョンを特定しておく

```
az account list-locations
```

たいてい[yourLocation]など日本の近いリージョンが使えると思うが、どこを使うかを決める。この後のコマンド全ての「[yourLocation]」で使用する。



## redmineの準備

1. redmineサーバーをAzure VMで作成
1. redmineの管理アカウントとテスト用ユーザーアカウントを作成
1. atomキーとapiキーを取得
1. テストで取得するために、プロジェクトとチケットを作成して添付ファイルを追加

### redmineサーバーをAzure VMで作成


※別ネットワークからのアクセスを想定した実験なので、パブリックIPを作成してNSGにも穴を空けてインターネットから通信できるようにする

#### RG作る

```
az group create --name RedmineTest1RG --subscription [yourSubscription] --location [yourLocation]
```

#### Bitnami redmineのOSイメージを探す

https://docs.microsoft.com/en-us/azure/virtual-machines/linux/cli-ps-findimage

Bitnami redmineのimage一覧を取る

```
az vm image list --all --location japaneast --publisher Bitnami --offer redmine --output table
```

例えば次のような出力がある。このurnをVM作成時のimageとする

```
Offer    Publisher    Sku    Urn                               Version
-------  -----------  -----  --------------------------------  --------------
redmine  Bitnami      3      Bitnami:redmine:3:5.0.2112467525  5.0.2112467525
```

#### VM作る

※このコマンドは特に時間がかかる。10分以上かかることが多い

```
az vm create --name RedmineTest1 --resource-group RedmineTest1RG --subscription [yourSubscription] --admin-username localadmin --authentication-type ssh --generate-ssh-keys --image Bitnami:redmine:3:5.0.2112467525 --location [yourLocation] --public-ip-address-dns-name [一意のドメイン 例：redmine-test1-server] --public-ip-sku Basic --public-ip-address-allocation dynamic --size Standard_B1s --storage-sku StandardSSD_LRS 
```


##### VM自動シャットダウン設定

```
az vm auto-shutdown --time 1200 --name RedmineTest1 --resource-group RedmineTest1RG --subscription [yourSubscription] --email [yourEmail]
```

※webhookを別途作成しておいて--webhookオプションで指定するとさらに便利


##### NSGにHTTPアクセスの許可を通す

###### 事前の情報取得

自動作成されたNSGの名前を取得する。そのために、まずはNICを取得する。

```
az vm nic list --vm-name RedmineTest1 --resource-group RedmineTest1RG --subscription [yourSubscription]
```

コマンドを実行すると、以下のようにNICを取得できる。

```
[
  {
    "deleteOption": null,
    "id": "/subscriptions/guid/resourceGroups/RedmineTest1RG/providers/Microsoft.Network/networkInterfaces/RedmineTest1VMNic",
    "primary": null,
    "resourceGroup": "RedmineTest1RG"
  }
]
```

このうち、次の部分がNICのID。これをメモし、この後のコマンドに使用する。：/subscriptions/guid/resourceGroups/RedmineTest1RG/providers/Microsoft.Network/networkInterfaces/RedmineTest1VMNic

そのNICの情報を取得する。

```
az vm nic show --nic /subscriptions/guid/resourceGroups/RedmineTest1RG/providers/Microsoft.Network/networkInterfaces/RedmineTest1VMNic --vm-name RedmineTest1 --resource-group RedmineTest1RG --subscription [yourSubscription]
```

色々な情報が取れる中に、次のように紐付いているNSGの情報が含まれている。

```
  "networkSecurityGroup": {
    "defaultSecurityRules": null,
    "etag": null,
    "flowLogs": null,
    "id": "/subscriptions/guid/resourceGroups/RedmineTest1RG/providers/Microsoft.Network/networkSecurityGroups/RedmineTest1NSG",
    "location": null,
    "name": null,
    "networkInterfaces": null,
    "provisioningState": null,
    "resourceGroup": "RedmineTest1RG",
    "resourceGuid": null,
    "securityRules": null,
    "subnets": null,
    "tags": null,
    "type": null
  },
```

このうち、次の部分がNSGのID。/subscriptions/guid/resourceGroups/RedmineTest1RG/providers/Microsoft.Network/networkSecurityGroups/RedmineTest1NSG

このうち末尾のRedmineTest1NSGがNSG名なのでこれをメモし、この後のコマンドに使用する。

###### ソースIP・宛先ポート指定でルール追加

取得したNSGに対して、80ポートのアクセス許可を追加する。yourIP部分に、アクセスを許可するIPアドレス（アクセス元）を指定する。

priorityの値は、NSG内で一意の番号で優先順位を指定する必要がある。この場合は新規作成後なので、1001で通るはず。

```
az network nsg rule create --nsg-name RedmineTest1NSG --resource-group RedmineTest1RG --subscription [yourSubscription] --name http-allow-ip-1 --priority 1001 --destination-port-ranges 80 --access Allow --protocol TCP --direction Inbound --source-address-prefixes [yourIP]
```

###### ルール削除

追加したルールを削除したい場合は、次のようにルール名を指定して削除する。

```
az network nsg rule delete --nsg-name RedmineTest1NSG --resource-group RedmineTest1RG --subscription [yourSubscription] --name http-allow-ip-1

```


※以下の記載は未作成

## DurableFunctions作成


1. VSのプロジェクト作成のテンプレートで、「Azure Functions」を選択
1. Functionの選択肢で「Dulable Functions Orchestration」を選択


## redmineからの情報取得


1. チェック対象の更新が含まれるようにしたRSSのURLと、RSSアクセス用のキーを用意
1.


## 取得した情報をCosmosDBへ保存



