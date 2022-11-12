open System
open System.IO
open System.Text.Json


/// Find the StDefinitions.cs file
let rec findPath searchPath = 
    let foundSrc =
        IO.Path.Combine(searchPath, "src")
        |> IO.Directory.Exists

    if foundSrc then 
        IO.Path.Combine(searchPath, "src", "StDefinitions.cs")
    else
        let parent = Directory.GetParent searchPath
        findPath parent.FullName
        
/// Download st definitions to json
let downloadDefinitions () =
    let request = System.Net.WebRequest.Create("https://raw.githubusercontent.com/XRPLF/xrpl.js/develop/packages/ripple-binary-codec/src/enums/definitions.json")
    use response = request.GetResponse()
    use stream = response.GetResponseStream()
    JsonDocument.Parse(stream)

let trimSt (str : string) =
    if str.StartsWith("ST") then
        str.Substring(2)
    else
        str

/// Emit the TYPES
let emitTypes (writer : TextWriter) (document : JsonDocument) =
    let types = document.RootElement.GetProperty("TYPES")
    
    writer.WriteLine("    /// <summary>")
    writer.WriteLine("    /// Map of data types to their \"type code\" for constructing field IDs and sorting fields in canonical order.")
    writer.WriteLine("    /// Codes below 1 should not appear in actual data;")
    writer.WriteLine("    /// codes above 10000 represent special \"high-level\" object types such as \"Transaction\" that cannot be serialized inside other objects.")
    writer.WriteLine("    /// See the Type List for details of how to serialize each type.")
    writer.WriteLine("    /// </summary>")
    writer.WriteLine("    public enum StTypeCode")
    writer.WriteLine("    {")
    for entry in types.EnumerateObject() do
        // trim ST off the front of types, we're nesting this in StTypeCode anyway
        let key = trimSt entry.Name
        let value = entry.Value.GetInt32()
        writer.WriteLine("        {0} = {1},", key, value)
    writer.WriteLine("    }")
    writer.WriteLine()

/// Emit the LEDGER_ENTRY_TYPES
let emitLedgerEntry (writer : TextWriter) (document : JsonDocument) =
    let types = document.RootElement.GetProperty("LEDGER_ENTRY_TYPES")
        
    writer.WriteLine("    /// <summary>")
    writer.WriteLine("    /// Map of ledger objects to their data type.")
    writer.WriteLine("    /// These appear in ledger state data, and in the \"affected nodes\" section of processed transactions' metadata.")
    writer.WriteLine("    /// </summary>")
    writer.WriteLine("    public enum StLedgerEntryType")
    writer.WriteLine("    {")
    for entry in types.EnumerateObject() do
        let value = entry.Value.GetInt32()
        writer.WriteLine("        {0} = {1},", entry.Name, value)
    writer.WriteLine("    }")
    writer.WriteLine()
    
/// Emit the TRANSACTION_TYPES
let emitTransactionType (writer : TextWriter) (document : JsonDocument) =
    let types = document.RootElement.GetProperty("TRANSACTION_TYPES")
    
    writer.WriteLine("    /// <summary>")
    writer.WriteLine("    /// The type of a transaction (TransactionType field) is the most fundamental information about a transaction.")
    writer.WriteLine("    /// This indicates what type of operation the transaction is supposed to do.")    
    writer.WriteLine("    /// </summary>")
    writer.WriteLine("    public enum StTransactionType : ushort")
    writer.WriteLine("    {")
    for entry in types.EnumerateObject() do
        let value = entry.Value.GetInt32()
        writer.WriteLine("        {0} = {1},", entry.Name, uint16 value)
    writer.WriteLine("    }")
    writer.WriteLine()

let getFields (document : JsonDocument) =
    document.RootElement.GetProperty("FIELDS").EnumerateArray()
    |> Seq.map(fun tuple ->
        let name = tuple.[0].GetString()
        let object = tuple.[1]
        let typ = object.GetProperty("type").GetString()
        let nth = object.GetProperty("nth").GetInt32()
        let isSigningField = object.GetProperty("isSigningField").GetBoolean()
        // trim ST off the front of types, we're nesting this in St anyway
        let key = trimSt typ
        name, (key, nth, isSigningField)
    )
    |> Map.ofSeq
    // Add on extra fields that ripple-binary-codec is missing
    |> Map.add "Version" ("UInt16", 16, true)
    
/// Emit the FIELDS
let emitFields (writer : TextWriter) (document : JsonDocument) =
    // We want to map by type, then map by name to get index
    let fields =
        getFields document
        |> Seq.groupBy (fun kv ->
            let (typ, _, _) = kv.Value
            typ)
        |> Map.ofSeq
        |> Map.map (fun _ seq ->
            seq
            |> Seq.map (fun kv ->
                let (_, nth, _) = kv.Value
                kv.Key, nth
            )
            |> Map.ofSeq)

    // For each type emit the field codes (eg. UInt8FieldCode.CloseResolution = 1)
    for typeFields in fields do
        writer.WriteLine("    public enum St{0}FieldCode", typeFields.Key)
        writer.WriteLine("    {")

        for field in typeFields.Value do 
            writer.WriteLine("        {0} = {1},", field.Key, field.Value)
        writer.WriteLine("    }")
        writer.WriteLine()

    // For each field emit the FieldId
    writer.WriteLine("    public partial struct StFieldId")
    writer.WriteLine("    {")
    for typeFields in fields do
        for field in typeFields.Value do 
            writer.WriteLine("        public static readonly StFieldId {0}_{1} = new StFieldId(StTypeCode.{0}, {2});", typeFields.Key, field.Key, uint field.Value)
        writer.WriteLine("")
    writer.WriteLine("    }")
    writer.WriteLine()

type LedgerField = { 
    Name : string
    OriginalType : string
    Type : string
    Optional : bool
    Doc : string
    Nth : int
    IsSigningField : bool
}
    
type LedgerType = { IsTransaction: bool; Name : string; Doc : string; Fields : LedgerField list }

let knownTypes = Map.ofList [
    // Directory nodes have AccountIds as hexstrings
    "Hash160_AccountId", (
        true, 
        "AccountId", 
        "new AccountId({0}.GetBytesFromBase16())", 
        "ToAccountId(reader.ReadHash160())",
        null
    )
    "AccountID", (
        true, 
        "AccountId", 
        "new AccountId({0}.GetString())", 
        "reader.ReadAccount()",
        "writer.WriteAccount({0}, {1})"
    )
    "Amount", (
        true, 
        "Amount", 
        "Ripple.Amount.ReadJson({0})", 
        "reader.ReadAmount()",
        "writer.WriteAmount({0}, {1})"
    )
    "XrpAmount", (
        true, 
        "XrpAmount", 
        "Ripple.XrpAmount.ReadJson({0})", 
        "reader.ReadXrpAmount()",
        "writer.WriteAmount({0}, {1})"
    )
    "IssuedAmount", (
        true, 
        "IssuedAmount", 
        "Ripple.IssuedAmount.ReadJson({0})", 
        "reader.ReadIssuedAmount()",
        "writer.WriteAmount({0}, {1})"
        )
    "UInt64", (
        true, 
        "ulong",
        "ulong.Parse({0}.GetString(), System.Globalization.NumberStyles.AllowHexSpecifier)", 
        "reader.ReadUInt64()",
        "writer.WriteUInt64({0}, {1})"
        )
    "UInt32", (
        true, 
        "uint", 
        "{0}.GetUInt32()", 
        "reader.ReadUInt32()",
        "writer.WriteUInt32({0}, {1})"
        )
    "UInt16", (
        true, 
        "ushort",
        "{0}.GetUInt16()",
        "reader.ReadUInt16()",
        "writer.WriteUInt16({0}, {1})"
        )
    "UInt8", (
        true, 
        "byte",
        "{0}.GetByte()", 
        "reader.ReadUInt8()",
        "writer.WriteUInt8({0}, {1})"
        )
    "Hash256", (
        true, 
        "Hash256",
        "new Hash256({0}.GetString())",
        "reader.ReadHash256()",
        "writer.WriteHash256({0}, {1})"
        )
    "Hash160_CurrencyCode", (
        true,
        "CurrencyCode", 
        "new CurrencyCode({0}.GetBytesFromBase16())", 
        "ToCurrencyCode(reader.ReadHash160())",
        null
        )
    "CurrencyCode", (
        true,
        "CurrencyCode",
        "new CurrencyCode({0}.GetString())",
        "reader.ReadCurrencyCode()",
        "writer.WriteCurrencyCode({0}, {1})"
        )
    "Hash128", (
        true, 
        "Hash128", 
        "new Hash128({0}.GetString())", 
        "reader.ReadHash128()",
        "writer.WriteHash128({0}, {1})"
        )
    "Blob", (
        true, 
        "ReadOnlyMemory<byte>",
        "{0}.GetBytesFromBase16()",
        "reader.ReadBlob()",
        "writer.WriteBlob({0}, {1}.Span)"
        )
    "Array<SignerEntry>", (
        false,
        "ReadOnlyCollection<SignerEntry>",
        "new SignerEntry({0})", 
        "new SignerEntry(ref reader)",
        "{0}.WriteTo(ref writer)"
        )
    "Array<DisabledValidator>", (
        false, 
        "ReadOnlyCollection<DisabledValidator>",
        "new DisabledValidator({0})", 
        "new DisabledValidator(ref reader)",
        null
        )
    "Vector256", (
        false,
        "ReadOnlyCollection<Hash256>",
        "new Hash256({0}.GetString())",
        "reader.ReadVector256()",
        null
        )
    "Array<Majority>", (
        false,
        "ReadOnlyCollection<Majority>",
        "new Majority({0})",
        "new Majority(ref reader)",
        null
        )
    "AccountRootFlags", (
        true, 
        "AccountRootFlags",
        "(AccountRootFlags){0}.GetUInt32()",
        "(AccountRootFlags)reader.ReadUInt32()",
        null
        )
    "RippleStateFlags", (
        true,
        "RippleStateFlags",
        "(RippleStateFlags){0}.GetUInt32()", 
        "(RippleStateFlags)reader.ReadUInt32()",
        null
        )
    "SignerListFlags", (
        true,
        "SignerListFlags",
        "(SignerListFlags){0}.GetUInt32()", 
        "(SignerListFlags)reader.ReadUInt32()",
        null
        )
    "OfferFlags", (
        true,
        "OfferFlags", 
        "(OfferFlags){0}.GetUInt32()", 
        "(OfferFlags)reader.ReadUInt32()",
        null
        )
    "DateTimeOffset", (
        true,
        "DateTimeOffset",
        "Epoch.ToDateTimeOffset({0}.GetUInt32())", 
        "Epoch.ToDateTimeOffset(reader.ReadUInt32())",
        "writer.WriteUInt32({0}, Epoch.FromDateTimeOffset({1}))"
        )
    "TimeSpan", (
        true, 
        "TimeSpan",
        "TimeSpan.FromSeconds({0}.GetUInt32())",
        "TimeSpan.FromSeconds(reader.ReadUInt32())",
        "writer.WriteUInt32({0}, (uint){1}.TotalSeconds)"
        )
    "AccountSetFlags", (
        true, 
        "AccountSetFlags",
        "(AccountSetFlags){0}.GetUInt32()", 
        "(AccountSetFlags)reader.ReadUInt32()",
        "writer.WriteUInt32({0}, (uint){1})"
        )
    "Array<Memo>", (
        false,
        "ReadOnlyCollection<Memo>",
        "new Memo({0})", 
        "new Memo(ref reader)",
        "{0}.WriteTo(ref writer)"
        )
    "Array<Signer>", (
        false,
        "ReadOnlyCollection<Signer>", 
        "new Signer({0})",
        "new Signer(ref reader)",
        "{0}.WriteTo(ref writer)"
        )
    "PathSet", (
        false,
        "PathSet",
        "new PathSet({0})",
        "reader.ReadPathSet()",
        "writer.WritePathSet({0}, {1})"
        )
]

let getFieldType (ledgerField : LedgerField) : string =
    let isValueType, netType, _, _, _ = knownTypes.[ledgerField.Type]
    if not ledgerField.Optional then
        netType
    else
        if isValueType then
            sprintf "%s?" netType
        else
            netType
            
let getInnerType (ledgerField : LedgerField) : string Option =
    if ledgerField.Type.StartsWith("Array<") then
        let _, collection, _, _, _ = knownTypes.[ledgerField.Type]
        let index = collection.IndexOf '<' + 1
        Some (collection.Substring(index, collection.Length - (index + 1)))
    elif ledgerField.Type = "Vector256" then
        Some "Hash256"
    else
        None

let isValueType (ledgerField : LedgerField) : bool =
    let isValueType, _, _, _, _= knownTypes.[ledgerField.Type]
    isValueType
        
let readJsonField (ledgerField : LedgerField) (json : string) : string =
    let _, _, jsonReader, _, _= knownTypes.[ledgerField.Type]
    String.Format(jsonReader, json)
            
let readStField (ledgerField : LedgerField) : string =
    let _, _, _, stReader, _ = knownTypes.[ledgerField.Type]
    stReader

let writeStField (ledgerField : LedgerField) : string =
    let _, _, _, _, stWriter = knownTypes.[ledgerField.Type]
    stWriter

/// Emit the LedgerObjects
let emitLedger (writer : TextWriter) (document : JsonDocument) =
    // Need the type index so we know the order to read ST fields in    
    let types = 
        document.RootElement.GetProperty("TYPES").EnumerateObject()
        |> Seq.map(fun kv -> trimSt kv.Name, kv.Value.GetInt32())
        |> Map.ofSeq

    // We want to map by name then by type
    let fields = getFields document
        
    let field name doc =
        let fieldType, nth, isSigningField = fields.[name]
        { 
            Name = name
            Doc = doc
            Nth = nth
            IsSigningField = isSigningField
            OriginalType = fieldType
            Type = fieldType 
            Optional = false
        }

    let fieldOpt name doc =
        let fieldType, nth, isSigningField = fields.[name]
        { 
            Name = name
            Doc = doc
            Nth = nth
            IsSigningField = isSigningField
            OriginalType = fieldType
            Type = fieldType 
            Optional = true
        }

    let withOverride (typeName : string) (field : LedgerField) = 
        { field with Type = typeName}

    // For each ledger type we want to emit each field, then the Json constructor then the StReader constructor.
    // We'll write the Id calculation functions manually
    
    let ownerNode = field "OwnerNode" "A hint indicating which page of the sender's owner directory links to this object, in case the directory consists of multiple pages. Note: The object does not contain a direct link to the owner directory containing it, since that value can be derived from the Account."
    let previousTxnID = field "PreviousTxnID" "The identifying hash of the transaction that most recently modified this object."
    let previousTxnLgrSeq = field "PreviousTxnLgrSeq" "The index of the ledger that contains the transaction that most recently modified this object."

    let ledgerTypes = [
        {
            IsTransaction = false
            Name = "AccountRoot"
            Doc = "The AccountRoot object type describes a single account, its settings, and XRP balance."
            Fields = [
                field "Account" "The identifying address of this account, such as rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn."
                field "Balance" "The account's current XRP balance in drops." |> withOverride "XrpAmount"
                field "Flags" "A bit-map of boolean flags enabled for this account." |> withOverride "AccountRootFlags"
                field "OwnerCount" "The number of objects this account owns in the ledger, which contributes to its owner reserve."
                previousTxnID
                previousTxnLgrSeq
                field "Sequence" "The sequence number of the next valid transaction for this account. (Each account starts with Sequence = 1 and increases each time a transaction is made.)"
                fieldOpt "AccountTxnID" "The identifying hash of the transaction most recently sent by this account. This field must be enabled to use the AccountTxnID transaction field. To enable it, send an AccountSet transaction with the asfAccountTxnID flag enabled."
                fieldOpt "Domain" "A domain associated with this account. In JSON, this is the hexadecimal for the ASCII representation of the domain."
                fieldOpt "EmailHash" "The md5 hash of an email address. Clients can use this to look up an avatar through services such as Gravatar."
                fieldOpt "MessageKey" "A public key that may be used to send encrypted messages to this account. In JSON, uses hexadecimal. Must be exactly 33 bytes, with the first byte indicating the key type: 0x02 or 0x03 for secp256k1 keys, 0xED for Ed25519 keys."
                fieldOpt "RegularKey" "The address of a key pair that can be used to sign transactions for this account instead of the master key. Use a SetRegularKey transaction to change this value."
                fieldOpt "TickSize" "How many significant digits to use for exchange rates of Offers involving currencies issued by this address. Valid values are 3 to 15, inclusive. (Added by the TickSize amendment.)"
                fieldOpt "TransferRate" "A transfer fee to charge other users for sending currency issued by this account to each other."
                fieldOpt "BurnedNFTokens" "How many total of this account's issued non-fungible tokens have been burned. This number is always equal or less than MintedNFTokens."
                fieldOpt "MintedNFTokens" "How many total non-fungible tokens have been minted by and on behalf of this account."
                fieldOpt "NFTokenMinter" "Another account that is authorized to mint non-fungible tokens on behalf of this account."
                fieldOpt "TicketCount" "How many significant digits to use for exchange rates of Offers involving currencies issued by this address. Valid values are 3 to 15, inclusive. (Added by the TickSize amendment.)"
            ]
        }
        {
            IsTransaction = false
            Name = "Amendments"
            Doc = "The Amendments object type contains a list of Amendments that are currently active. Each ledger version contains at most one Amendments object."
            Fields = [
                fieldOpt "Amendments" "Array of 256-bit amendment IDs for all currently-enabled amendments. If omitted, there are no enabled amendments."
                fieldOpt "Majorities" "Array of objects describing the status of amendments that have majority support but are not yet enabled. If omitted, there are no pending amendments with majority support." |> withOverride "Array<Majority>"
                field "Flags" "A bit-map of boolean flags. No flags are defined for the Amendments object type, so this value is always 0."
            ]
        }
        {
            IsTransaction = false
            Name = "Check"
            Doc = "A Check object describes a check, similar to a paper personal check, which can be cashed by its destination to get money from its sender. (The potential payment has already been approved by its sender, but no money moves until it is cashed. Unlike an Escrow, the money for a Check is not set aside, so cashing the Check could fail due to lack of funds.)"
            Fields = [
                field "Account" "The sender of the Check. Cashing the Check debits this address's balance."
                field "Destination" "The intended recipient of the Check. Only this address can cash the Check, using a CheckCash transaction."
                field "Flags" "A bit-map of boolean flags. No flags are defined for Checks, so this value is always 0."
                ownerNode
                previousTxnID
                previousTxnLgrSeq
                field "SendMax" "The maximum amount of currency this Check can debit the sender. If the Check is successfully cashed, the destination is credited in the same currency for up to this amount."
                field "Sequence" "The sequence number of the CheckCreate transaction that created this check."
                fieldOpt "DestinationNode" "A hint indicating which page of the destination's owner directory links to this object, in case the directory consists of multiple pages."
                fieldOpt "DestinationTag" "An arbitrary tag to further specify the destination for this Check, such as a hosted recipient at the destination address."
                fieldOpt "Expiration" "Indicates the time after which this Check is considered expired. See Specifying Time for details." |> withOverride "DateTimeOffset"
                fieldOpt "InvoiceID" "Arbitrary 256-bit hash provided by the sender as a specific reason or identifier for this Check."
                fieldOpt "SourceTag" "An arbitrary tag to further specify the source for this Check, such as a hosted recipient at the sender's address."
            ]
        }
        {
            IsTransaction = false
            Name = "DepositPreauth"
            Doc = "A DepositPreauth object tracks a preauthorization from one account to another. DepositPreauth transactions create these objects."
            Fields = [
                field "Account" "The account that granted the preauthorization. (The destination of the preauthorized payments.)"
                field "Authorize" "The account that received the preauthorization. (The sender of the preauthorized payments.)"
                field "Flags" "A bit-map of boolean flags. No flags are defined for DepositPreauth objects, so this value is always 0."
                ownerNode
                previousTxnID
                previousTxnLgrSeq
            ]
        }
        {
            IsTransaction = false
            Name = "DirectoryNode"
            Doc = "The DirectoryNode object type provides a list of links to other objects in the ledger's state tree. A single conceptual Directory takes the form of a doubly linked list, with one or more DirectoryNode objects each containing up to 32 IDs of other objects. The first object is called the root of the directory, and all objects other than the root object can be added or deleted as necessary."
            Fields = [
                field "Flags" "A bit-map of boolean flags enabled for this directory. Currently, the protocol defines no flags for DirectoryNode objects."
                field "RootIndex" "The ID of root object for this directory."
                field "Indexes" "The contents of this Directory: an array of IDs of other objects."
                fieldOpt "IndexNext" "If this Directory consists of multiple pages, this ID links to the next object in the chain, wrapping around at the end."
                fieldOpt "IndexPrevious" "If this Directory consists of multiple pages, this ID links to the previous object in the chain, wrapping around at the beginning."
                fieldOpt "Owner" "(Owner Directories only) The address of the account that owns the objects in this directory."
                fieldOpt "ExchangeRate" "(Offer Directories only) DEPRECATED. Do not use."
                fieldOpt "TakerPaysCurrency" "(Offer Directories only) The currency code of the TakerPays amount from the offers in this directory." |> withOverride "Hash160_CurrencyCode"
                fieldOpt "TakerPaysIssuer" "(Offer Directories only) The issuer of the TakerPays amount from the offers in this directory." |> withOverride "Hash160_AccountId"
                fieldOpt "TakerGetsCurrency" "(Offer Directories only) The currency code of the TakerGets amount from the offers in this directory." |> withOverride "Hash160_CurrencyCode"
                fieldOpt "TakerGetsIssuer" "(Offer Directories only) The issuer of the TakerGets amount from the offers in this directory." |> withOverride "Hash160_AccountId"
            ]
        }
        {
            IsTransaction = false
            Name = "Escrow"
            Doc = "The Escrow object type represents a held payment of XRP waiting to be executed or canceled. An EscrowCreate transaction creates an Escrow object in the ledger."
            Fields = [
                field "Account" "The address of the owner (sender) of this held payment. This is the account that provided the XRP, and gets it back if the held payment is canceled."
                field "Destination" "The destination address where the XRP is paid if the held payment is successful."
                field "Amount" "The amount of XRP, in drops, to be delivered by the held payment."
                fieldOpt "Condition" "A PREIMAGE-SHA-256 crypto-condition , as hexadecimal. If present, the EscrowFinish transaction must contain a fulfillment that satisfies this condition."
                fieldOpt "CancelAfter" "The held payment can be canceled if and only if this field is present and the time it specifies has passed. Specifically, this is specified as seconds since the Ripple Epoch and it \"has passed\" if it's earlier than the close time of the previous validated ledger." |> withOverride "DateTimeOffset"
                fieldOpt "FinishAfter" "The time, in seconds since the Ripple Epoch, after which this held payment can be finished. Any EscrowFinish transaction before this time fails. (Specifically, this is compared with the close time of the previous validated ledger.)" |> withOverride "DateTimeOffset"
                field "Flags" "A bit-map of boolean flags. No flags are defined for the Escrow type, so this value is always 0."
                fieldOpt "SourceTag" "An arbitrary tag to further specify the source for this held payment, such as a hosted recipient at the owner's address."
                fieldOpt "DestinationTag" "An arbitrary tag to further specify the destination for this held payment, such as a hosted recipient at the destination address."
                fieldOpt "DestinationNode" "A hint indicating which page of the destination's owner directory links to this object, in case the directory consists of multiple pages. Omitted on escrows created before enabling the fix1523 amendment."
                ownerNode
                previousTxnID
                previousTxnLgrSeq
            ]
        }
        {
            IsTransaction = false
            Name = "FeeSettings"
            Doc = "The FeeSettings object type contains the current base transaction cost and reserve amounts as determined by fee voting. Each ledger version contains at most one FeeSettings object."
            Fields = [
                field "BaseFee" "The transaction cost of the \"reference transaction\" in drops of XRP as hexadecimal."
                field "ReferenceFeeUnits" "The BaseFee translated into \"fee units\"."
                field "ReserveBase" "The base reserve for an account in the XRP Ledger, as drops of XRP."
                field "ReserveIncrement" "The incremental owner reserve for owning objects, as drops of XRP."
                field "Flags" "A bit-map of boolean flags for this object. No flags are defined for this type."
            ]
        }
        {
            IsTransaction = false
            Name = "LedgerHashes"
            Doc = "The LedgerHashes object type contains a history of prior ledgers that led up to this ledger version, in the form of their hashes. Objects of this ledger type are modified automatically in the process of closing a ledger."
            Fields = [            
                field "LastLedgerSequence" "The Ledger Index of the last entry in this object's Hashes array."
                field "Hashes" "An array of up to 256 ledger hashes. The contents depend on which sub-type of LedgerHashes object this is."
                field "Flags" "A bit-map of boolean flags for this object. No flags are defined for this type."            
            ]
        }
        {
            IsTransaction = false
            Name = "NegativeUNL"
            Doc = "The NegativeUNL object type contains the current status of the Negative UNL, a list of trusted validators currently believed to be offline."
            Fields = [
                fieldOpt "DisabledValidators" "A list of DisabledValidator objects (see below), each representing a trusted validator that is currently disabled." |> withOverride "Array<DisabledValidator>"
                fieldOpt "ValidatorToDisable" "The public key of a trusted validator that is scheduled to be disabled in the next flag ledger."
                fieldOpt "ValidatorToReEnable" "The public key of a trusted validator in the Negative UNL that is scheduled to be re-enabled in the next flag ledger."
            ]
        }
        {
            IsTransaction = false
            Name = "Offer"
            Doc = "The Offer object type describes an offer to exchange currencies, more traditionally known as an order, in the XRP Ledger's distributed exchange. An OfferCreate transaction only creates an Offer object in the ledger when the offer cannot be fully executed immediately by consuming other offers already in the ledger."
            Fields = [
                field "Flags" "A bit-map of boolean flags enabled for this offer." |> withOverride "OfferFlags"
                field "Account" "The address of the account that owns this offer."
                field "Sequence" "The Sequence value of the OfferCreate transaction that created this Offer object. Used in combination with the Account to identify this Offer."
                field "TakerPays" "The remaining amount and type of currency requested by the offer creator."
                field "TakerGets" "The remaining amount and type of currency being provided by the offer creator."
                field "BookDirectory" "The ID of the Offer Directory that links to this offer."
                field "BookNode" "A hint indicating which page of the offer directory links to this object, in case the directory consists of multiple pages."
                fieldOpt "Expiration" "Indicates the time after which this offer is considered unfunded. See Specifying Time for details." |> withOverride "DateTimeOffset"
                ownerNode
                previousTxnID
                previousTxnLgrSeq
            ]
        }
        {
            IsTransaction = false
            Name = "PayChannel"
            Doc = "The PayChannel object type represents a payment channel. Payment channels enable small, rapid off-ledger payments of XRP that can be later reconciled with the consensus ledger. A payment channel holds a balance of XRP that can only be paid out to a specific destination address until the channel is closed. Any unspent XRP is returned to the channel's owner (the source address that created and funded it) when the channel closes."
            Fields = [
                field "Account" "The source address that owns this payment channel. This comes from the sending address of the transaction that created the channel."
                field "Destination" "The destination address for this payment channel. While the payment channel is open, this address is the only one that can receive XRP from the channel. This comes from the Destination field of the transaction that created the channel."
                field "Amount" "Total XRP, in drops, that has been allocated to this channel. This includes XRP that has been paid to the destination address. This is initially set by the transaction that created the channel and can be increased if the source address sends a PaymentChannelFund transaction."
                field "Balance" "Total XRP, in drops, already paid out by the channel. The difference between this value and the Amount field is how much XRP can still be paid to the destination address with PaymentChannelClaim transactions. If the channel closes, the remaining difference is returned to the source address."
                field "PublicKey" "Public key, in hexadecimal, of the key pair that can be used to sign claims against this channel. This can be any valid secp256k1 or Ed25519 public key. This is set by the transaction that created the channel and must match the public key used in claims against the channel. The channel source address can also send XRP from this channel to the destination without signed claims."
                field "SettleDelay" "Number of seconds the source address must wait to close the channel if it still has any XRP in it. Smaller values mean that the destination address has less time to redeem any outstanding claims after the source address requests to close the channel. Can be any value that fits in a 32-bit unsigned integer (0 to 2^32-1). This is set by the transaction that creates the channel." |> withOverride "TimeSpan"
                ownerNode
                previousTxnID
                previousTxnLgrSeq
                field "Flags" "A bit-map of boolean flags enabled for this payment channel. Currently, the protocol defines no flags for PayChannel objects."
                fieldOpt "DestinationNode" "A hint indicating which page of the destination's owner directory links to this object, in case the directory consists of multiple pages."
                fieldOpt "Expiration" "The mutable expiration time for this payment channel, in seconds since the Ripple Epoch. The channel is expired if this value is present and smaller than the previous ledger's close_time field. See Setting Channel Expiration for more details." |> withOverride "DateTimeOffset"
                fieldOpt "CancelAfter" "The immutable expiration time for this payment channel, in seconds since the Ripple Epoch. This channel is expired if this value is present and smaller than the previous ledger's close_time field. This is optionally set by the transaction that created the channel, and cannot be changed." |> withOverride "DateTimeOffset"
                fieldOpt "SourceTag" "An arbitrary tag to further specify the source for this payment channel, such as a hosted recipient at the owner's address."
                fieldOpt "DestinationTag" "An arbitrary tag to further specify the destination for this payment channel, such as a hosted recipient at the destination address."
            ]
        }
        {
            IsTransaction = false
            Name = "RippleState"
            Doc = "The RippleState object type connects two accounts in a single currency. Conceptually, a RippleState object represents two trust lines between the accounts, one from each side. Each account can change the settings for its side of the RippleState object, but the balance is a single shared value. A trust line that is entirely in its default state is considered the same as a trust line that does not exist, so rippled deletes RippleState objects when their properties are entirely default."
            Fields = [
                field "Flags" "A bit-map of boolean options enabled for this object." |> withOverride "RippleStateFlags"
                field "Balance" "The balance of the trust line, from the perspective of the low account. A negative balance indicates that the low account has issued currency to the high account. The issuer in this is always set to the neutral value ACCOUNT_ONE." |> withOverride "IssuedAmount"
                field "LowLimit" "The limit that the low account has set on the trust line. The issuer is the address of the low account that set this limit." |> withOverride "IssuedAmount"
                field "HighLimit" "The limit that the high account has set on the trust line. The issuer is the address of the high account that set this limit." |> withOverride "IssuedAmount"
                previousTxnID
                previousTxnLgrSeq
                field "LowNode" "(Omitted in some historical ledgers) A hint indicating which page of the low account's owner directory links to this object, in case the directory consists of multiple pages."
                field "HighNode" "(Omitted in some historical ledgers) A hint indicating which page of the high account's owner directory links to this object, in case the directory consists of multiple pages."
                fieldOpt "LowQualityIn" "The inbound quality set by the low account, as an integer in the implied ratio LowQualityIn:1,000,000,000. As a special case, the value 0 is equivalent to 1 billion, or face value."
                fieldOpt "LowQualityOut" "The outbound quality set by the low account, as an integer in the implied ratio LowQualityOut:1,000,000,000. As a special case, the value 0 is equivalent to 1 billion, or face value."
                fieldOpt "HighQualityIn" "The inbound quality set by the high account, as an integer in the implied ratio HighQualityIn:1,000,000,000. As a special case, the value 0 is equivalent to 1 billion, or face value."
                fieldOpt "HighQualityOut" "The outbound quality set by the high account, as an integer in the implied ratio HighQualityOut:1,000,000,000. As a special case, the value 0 is equivalent to 1 billion, or face value."
            ]
        }
        {
            IsTransaction = false
            Name = "SignerList"
            Doc = "The SignerList object type represents a list of parties that, as a group, are authorized to sign a transaction in place of an individual account. You can create, replace, or remove a signer list using a SignerListSet transaction."
            Fields = [
            
                field "Flags" "A bit-map of Boolean flags enabled for this signer list. For more information, see SignerList Flags." |> withOverride "SignerListFlags"
                ownerNode
                previousTxnID
                previousTxnLgrSeq
                field "SignerEntries" "An array of Signer Entry objects representing the parties who are part of this signer list." |> withOverride "Array<SignerEntry>"
                field "SignerListID" "An ID for this signer list. Currently always set to 0. If a future amendment allows multiple signer lists for an account, this may change."
                field "SignerQuorum" "A target number for signer weights. To produce a valid signature for the owner of this SignerList, the signers must provide valid signatures whose weights sum to this value or more."
            ]
        }
        {
            IsTransaction = false
            Name = "Ticket"
            Doc = "The Ticket object type represents a Ticket, which tracks an account sequence number that has been set aside for future use. You can create new tickets with a TicketCreate transaction. New in: rippled 1.7.0 "
            Fields = [
                field "Account" "The account that owns this Ticket."
                field "Flags" "A bit-map of Boolean flags enabled for this Ticket. Currently, there are no flags defined for Tickets."
                field "TicketSequence" "The Sequence Number this Ticket sets aside."
                ownerNode
                previousTxnID
                previousTxnLgrSeq
            ]
        }
        {
            IsTransaction = true
            Name = "AccountSet"
            Doc = "An AccountSet transaction modifies the properties of an account in the XRP Ledger."
            Fields = [
                fieldOpt "ClearFlag" "Unique identifier of a flag to disable for this account." |> withOverride "AccountSetFlags"
                fieldOpt "Domain" "The domain that owns this account, as a string of hex representing the ASCII for the domain in lowercase. Cannot be more than 256 bytes in length. "
                fieldOpt "EmailHash" "Hash of an email address to be used for generating an avatar image. Conventionally, clients use Gravatar to display this image."
                fieldOpt "MessageKey" "Public key for sending encrypted messages to this account. To set the key, it must be exactly 33 bytes, with the first byte indicating the key type: 0x02 or 0x03 for secp256k1 keys, 0xED for Ed25519 keys. To remove the key, use an empty value."
                fieldOpt "SetFlag" "Integer flag to enable for this account." |> withOverride "AccountSetFlags"
                fieldOpt "TransferRate" "The fee to charge when users transfer this account's issued currencies, represented as billionths of a unit. Cannot be more than 2000000000 or less than 1000000000, except for the special case 0 meaning no fee."
                fieldOpt "TickSize" "Tick size to use for offers involving a currency issued by this address. The exchange rates of those offers is rounded to this many significant digits. Valid values are 3 to 15 inclusive, or 0 to disable. (Added by the TickSize amendment.)"
            ]
        }
        {
            IsTransaction = true
            Name = "AccountDelete"
            Doc = "An AccountDelete transaction deletes an account and any objects it owns in the XRP Ledger, if possible, sending the account's remaining XRP to a specified destination account. See Deletion of Accounts for the requirements to delete an account."
            Fields = [
                field "Destination" "The address of an account to receive any leftover XRP after deleting the sending account. Must be a funded account in the ledger, and must not be the sending account."
                fieldOpt "DestinationTag" "Arbitrary destination tag that identifies a hosted recipient or other information for the recipient of the deleted account's leftover XRP."
            ]
        }
        {
            IsTransaction = true
            Name = "CheckCancel"
            Doc = "Cancels an unredeemed Check, removing it from the ledger without sending any money. The source or the destination of the check can cancel a Check at any time using this transaction type. If the Check has expired, any address can cancel it."
            Fields = [
                field "CheckID" "The ID of the Check ledger object to cancel, as a 64-character hexadecimal string."
            ]
        }
        {
            IsTransaction = true
            Name = "CheckCash"
            Doc = "Attempts to redeem a Check object in the ledger to receive up to the amount authorized by the corresponding CheckCreate transaction. Only the Destination address of a Check can cash it with a CheckCash transaction. Cashing a check this way is similar to executing a Payment initiated by the destination."
            Fields = [
                field "CheckID" "The ID of the Check ledger object to cash, as a 64-character hexadecimal string."
                fieldOpt "Amount" "Redeem the Check for exactly this amount, if possible. The currency must match that of the SendMax of the corresponding CheckCreate transaction. You must provide either this field or DeliverMin."
                fieldOpt "DeliverMin" "Redeem the Check for at least this amount and for as much as possible. The currency must match that of the SendMax of the corresponding CheckCreate transaction. You must provide either this field or Amount."
            ]
        }
        {
            IsTransaction = true
            Name = "CheckCreate"
            Doc = "Create a Check object in the ledger, which is a deferred payment that can be cashed by its intended destination. The sender of this transaction is the sender of the Check."
            Fields = [
                field "Destination" "The unique address of the account that can cash the Check."
                field "SendMax" "Maximum amount of source currency the Check is allowed to debit the sender, including transfer fees on non-XRP currencies. The Check can only credit the destination with the same currency (from the same issuer, for non-XRP currencies). For non-XRP amounts, the nested field names MUST be lower-case."
                fieldOpt "DestinationTag" "Arbitrary tag that identifies the reason for the Check, or a hosted recipient to pay."
                fieldOpt "Expiration" "Time after which the Check is no longer valid, in seconds since the Ripple Epoch." |> withOverride "DateTimeOffset"
                fieldOpt "InvoiceID" "Arbitrary 256-bit hash representing a specific reason or identifier for this Check."
            ]
        }
        {
            IsTransaction = true
            Name = "DepositPreauth"
            Doc = "A DepositPreauth transaction gives another account pre-approval to deliver payments to the sender of this transaction. This is only useful if the sender of this transaction is using (or plans to use) Deposit Authorization."
            Fields = [
                fieldOpt "Authorize" "The XRP Ledger address of the sender to preauthorize."
                fieldOpt "Unauthorize" "The XRP Ledger address of a sender whose preauthorization should be revoked."
            ]
        }
        {
            IsTransaction = true
            Name = "EscrowCancel"
            Doc = "Return escrowed XRP to the sender."
            Fields = [
                field "Owner" "Address of the source account that funded the escrow payment."
                field "OfferSequence" "Transaction sequence (or Ticket number) of EscrowCreate transaction that created the escrow to cancel."
            ] 
        }
        {
            IsTransaction = true
            Name = "EscrowCreate"
            Doc = "Sequester XRP until the escrow process either finishes or is canceled."
            Fields = [
                field "Amount" "Amount of XRP, in drops, to deduct from the sender's balance and escrow. Once escrowed, the XRP can either go to the Destination address (after the FinishAfter time) or returned to the sender (after the CancelAfter time)."
                |> withOverride "XrpAmount"
                field "Destination" "Address to receive escrowed XRP."
                fieldOpt "CancelAfter" "The time, in seconds since the Ripple Epoch, when this escrow expires. This value is immutable; the funds can only be returned the sender after this time." |> withOverride "DateTimeOffset"
                fieldOpt "FinishAfter" "The time, in seconds since the Ripple Epoch, when the escrowed XRP can be released to the recipient. This value is immutable; the funds cannot move until this time is reached." |> withOverride "DateTimeOffset"
                fieldOpt "Condition" "Hex value representing a PREIMAGE-SHA-256 crypto-condition. The funds can only be delivered to the recipient if this condition is fulfilled."
                fieldOpt "DestinationTag" "Arbitrary tag to further specify the destination for this escrowed payment, such as a hosted recipient at the destination address."
            ]
        }
        {
            IsTransaction = true
            Name = "EscrowFinish"
            Doc = "Deliver XRP from a held payment to the recipient."
            Fields = [
                field "Owner" "Address of the source account that funded the held payment."
                field "OfferSequence" "Transaction sequence of EscrowCreate transaction that created the held payment to finish."
                fieldOpt "Condition" "Hex value matching the previously-supplied PREIMAGE-SHA-256 crypto-condition of the held payment."
                fieldOpt "Fulfillment" "Hex value of the PREIMAGE-SHA-256 crypto-condition fulfillment matching the held payment's Condition."
            ]
        }
        {
            IsTransaction = true
            Name = "OfferCancel"
            Doc = "An OfferCancel transaction removes an Offer object from the XRP Ledger."
            Fields = [
                field "OfferSequence" "The sequence number (or Ticket  number) of a previous OfferCreate transaction. If specified, cancel any offer object in the ledger that was created by that transaction. It is not considered an error if the offer specified does not exist."
            ]
        }
        {
            IsTransaction = true
            Name = "OfferCreate"
            Doc = "An OfferCreate transaction is effectively a limit order. It defines an intent to exchange currencies, and creates an Offer object if not completely fulfilled when placed. Offers can be partially fulfilled."
            Fields = [
                field "TakerGets" "The amount and type of currency being provided by the offer creator."
                field "TakerPays" "The amount and type of currency being requested by the offer creator."
                fieldOpt "Expiration" "Time after which the offer is no longer active, in seconds since the Ripple Epoch." |> withOverride "DateTimeOffset"
                fieldOpt "OfferSequence" "An offer to delete first, specified in the same way as OfferCancel."
            ]
        }
        {
            IsTransaction = true
            Name = "Payment"
            Doc = "A Payment transaction represents a transfer of value from one account to another. (Depending on the path taken, this can involve additional exchanges of value, which occur atomically.) This transaction type can be used for several types of payments."
            Fields = [
                field "Amount" "The amount of currency to deliver. For non-XRP amounts, the nested field names MUST be lower-case. If the tfPartialPayment flag is set, deliver up to this amount instead."    
                field "Destination" "The unique address of the account receiving the payment."
                fieldOpt "DestinationTag" "Arbitrary tag that identifies the reason for the payment to the destination, or a hosted recipient to pay."
                fieldOpt "InvoiceID" "Arbitrary 256-bit hash representing a specific reason or identifier for this payment."
                fieldOpt "Paths" "Array of payment paths to be used for this transaction. Must be omitted for XRP-to-XRP transactions."
                fieldOpt "SendMax" "Highest amount of source currency this transaction is allowed to cost, including transfer fees, exchange rates, and slippage. Does not include the XRP destroyed as a cost for submitting the transaction. For non-XRP amounts, the nested field names MUST be lower-case. Must be supplied for cross-currency/cross-issue payments. Must be omitted for XRP-to-XRP payments."
                fieldOpt "DeliverMin" "Minimum amount of destination currency this transaction should deliver. Only valid if this is a partial payment. For non-XRP amounts, the nested field names are lower-case."
            ]
        }
        {
            IsTransaction = true
            Name = "PaymentChannelClaim"
            Doc = "Claim XRP from a payment channel, adjust the payment channel's expiration, or both."
            Fields = [
                field "Channel" "The unique ID of the channel, as a 64-character hexadecimal string."
                fieldOpt "Balance" "Total amount of XRP, in drops, delivered by this channel after processing this claim. Required to deliver XRP. Must be more than the total amount delivered by the channel so far, but not greater than the Amount of the signed claim. Must be provided except when closing the channel."
                |> withOverride "XrpAmount"
                fieldOpt "Amount" "The amount of XRP, in drops, authorized by the Signature. This must match the amount in the signed message. This is the cumulative amount of XRP that can be dispensed by the channel, including XRP previously redeemed."
                |> withOverride "XrpAmount"
                fieldOpt "Signature" "The signature of this claim, as hexadecimal. The signed message contains the channel ID and the amount of the claim. Required unless the sender of the transaction is the source address of the channel."
                fieldOpt "PublicKey" "The public key used for the signature, as hexadecimal. This must match the PublicKey stored in the ledger for the channel. Required unless the sender of the transaction is the source address of the channel and the Signature field is omitted. (The transaction includes the public key so that rippled can check the validity of the signature before trying to apply the transaction to the ledger.)"
            ]
        }
        {
            IsTransaction = true
            Name = "PaymentChannelCreate"
            Doc = "Create a unidirectional channel and fund it with XRP. The address sending this transaction becomes the \"source address\" of the payment channel."
            Fields = [
                field "Amount" "Amount of XRP, in drops, to deduct from the sender's balance and set aside in this channel. While the channel is open, the XRP can only go to the Destination address. When the channel closes, any unclaimed XRP is returned to the source address's balance."
                |> withOverride "XrpAmount"
                field "Destination" "Address to receive XRP claims against this channel. This is also known as the \"destination address\" for the channel. Cannot be the same as the sender (Account)."
                field "SettleDelay" "Amount of time the source address must wait before closing the channel if it has unclaimed XRP." |> withOverride "TimeSpan"
                field "PublicKey" "The public key of the key pair the source will use to sign claims against this channel, in hexadecimal. This can be any secp256k1 or Ed25519 public key."
                fieldOpt "CancelAfter" "The time, in seconds since the Ripple Epoch, when this channel expires. Any transaction that would modify the channel after this time closes the channel without otherwise affecting it. This value is immutable; the channel can be closed earlier than this time but cannot remain open after this time." |> withOverride "DateTimeOffset"
                fieldOpt "DestinationTag" "Arbitrary tag to further specify the destination for this payment channel, such as a hosted recipient at the destination address."
            ]
        }
        {
            IsTransaction = true
            Name = "PaymentChannelFund"
            Doc = "Add additional XRP to an open payment channel, and optionally update the expiration time of the channel. Only the source address of the channel can use this transaction."
            Fields = [
                field "Channel" "The unique ID of the channel, as a 64-character hexadecimal string."
                field "Amount" "Amount of XRP, in drops to add to the channel. Must be a positive amount of XRP."
                |> withOverride "XrpAmount"
                fieldOpt "Expiration" "New Expiration time to set for the channel, in seconds since the Ripple Epoch. This must be later than either the current time plus the SettleDelay of the channel, or the existing Expiration of the channel. After the Expiration time, any transaction that would access the channel closes the channel without taking its normal action. Any unspent XRP is returned to the source address when the channel closes. (Expiration is separate from the channel's immutable CancelAfter time.) For more information, see the PayChannel ledger object type." |> withOverride "DateTimeOffset"
            ]
        }
        {
            IsTransaction = true
            Name = "SetRegularKey"
            Doc = "A SetRegularKey transaction assigns, changes, or removes the regular key pair associated with an account."
            Fields = [
                fieldOpt "RegularKey" "A base-58-encoded Address that indicates the regular key pair to be assigned to the account. If omitted, removes any existing regular key pair from the account. Must not match the master key pair for the address."
            ]
        }
        {
            IsTransaction = true
            Name = "SignerListSet"
            Doc = "The SignerListSet transaction creates, replaces, or removes a list of signers that can be used to multi-sign a transaction. This transaction type was introduced by the MultiSign amendment. New in: rippled 0.31.0"
            Fields = [
                field "SignerQuorum" "A target number for the signer weights. A multi-signature from this list is valid only if the sum weights of the signatures provided is greater than or equal to this value. To delete a signer list, use the value 0."
                field "SignerEntries" "(Omitted when deleting) Array of SignerEntry objects, indicating the addresses and weights of signers in this list. This signer list must have at least 1 member and no more than 8 members. No address may appear more than once in the list, nor may the Account submitting the transaction appear in the list."
                |> withOverride "Array<SignerEntry>"
            ]
        }
        {
            IsTransaction = true
            Name = "TicketCreate"
            Doc = "A TicketCreate transaction sets aside one or more sequence numbers as Tickets."
            Fields = [
                field "TicketCount" "How many Tickets to create. This must be a positive number and cannot cause the account to own more than 250 Tickets after executing this transaction."
            ]
        }
        {
            IsTransaction = true
            Name = "TrustSet"
            Doc = "Create or modify a trust line linking two accounts."
            Fields = [
                field "LimitAmount" "Object defining the trust line to create or modify, in the format of a Currency Amount." |> withOverride "IssuedAmount"
                fieldOpt "QualityIn" "Value incoming balances on this trust line at the ratio of this number per 1,000,000,000 units. A value of 0 is shorthand for treating balances at face value."
                fieldOpt "QualityOut" "Value outgoing balances on this trust line at the ratio of this number per 1,000,000,000 units. A value of 0 is shorthand for treating balances at face value."

            ]
        }
    ]

    // We need these field definitions but we're not going to emit properties for them so doc doesn't need to be filled in
    let transactionFields = [
        field "Account" null
        field "Fee" null |> withOverride "XrpAmount"
        field "Sequence" null
        fieldOpt "AccountTxnID" null
        fieldOpt "Flags" null
        fieldOpt "LastLedgerSequence" null 
        fieldOpt "Memos" null |> withOverride "Array<Memo>"
        fieldOpt "Signers" null |> withOverride "Array<Signer>"
        fieldOpt "SourceTag" null
        field "SigningPubKey" null
        fieldOpt "TicketSequence" null
        fieldOpt "TxnSignature" null
    ]

    // Write out the ReadJson method for each Transaction object
    writer.WriteLine("    public abstract partial class Transaction")
    writer.WriteLine("    {")

    writer.WriteLine("      /// <summary>")
    writer.WriteLine("      /// Read a transaction from a json object.")
    writer.WriteLine("      /// </summary>")
    writer.WriteLine("      public static Transaction ReadJson(JsonElement json)")
    writer.WriteLine("      {")
    writer.WriteLine("          var transactionType = json.GetProperty(\"TransactionType\").GetString();")

    let mutable elseIf = "if "
    for ledgerType in ledgerTypes do
        if ledgerType.IsTransaction then
            writer.WriteLine("          {0}(transactionType == \"{1}\")", elseIf, ledgerType.Name)
            writer.WriteLine("          {")
            writer.WriteLine("              return new {0}Transaction(json);", ledgerType.Name);
            writer.WriteLine("          }")
            elseIf <- "else if "

    writer.WriteLine("          else")
    writer.WriteLine("          {")
    writer.WriteLine("              throw new NotImplementedException(")
    writer.WriteLine("                  string.Format(\"Transaction type '{0}' not implemented\", transactionType));")
    writer.WriteLine("          }")
    writer.WriteLine("      }")
    writer.WriteLine("    }")

    for ledgerType in ledgerTypes do

        let allFields = 
            ledgerType.Fields @ (if ledgerType.IsTransaction then transactionFields else [])

        let ledgerTypeClassName = sprintf "%s%s" ledgerType.Name (if ledgerType.IsTransaction then "Transaction" else "LedgerEntry")

        
        writer.WriteLine("    /// <summary>")
        writer.WriteLine("    /// {0}", ledgerType.Doc)
        writer.WriteLine("    /// </summary>")
        writer.WriteLine("    public sealed partial class {0} : {1}", ledgerTypeClassName, if ledgerType.IsTransaction then "Transaction" else "LedgerObject")
        writer.WriteLine("    {")
        for field in ledgerType.Fields do
            // Need to override Ammendments because it can't be the same name as the type
            let fieldName = if field.Name = "Amendments" then "AmendmentIDs" else field.Name

            writer.WriteLine("        /// <summary>")
            let opt = if field.Optional then "(Optional) " else ""
            writer.WriteLine("        /// {0}{1}", opt, field.Doc)
            writer.WriteLine("        /// </summary>")
            writer.WriteLine("        public {0} {1} {{ get; {2}set; }}", getFieldType field, fieldName, if ledgerType.IsTransaction then "" else "private ");
            writer.WriteLine()

        // Transaction empty constructor
        if ledgerType.IsTransaction then
            writer.WriteLine("        public {0}()", ledgerTypeClassName)
            writer.WriteLine("        {")
            writer.WriteLine("        }")
            writer.WriteLine()

        let getFieldName (field : LedgerField) =
            if field.Name = "Amendments" then 
                "AmendmentIDs" 
            elif transactionFields |> List.contains field && field.Name = "Flags" then
                "base.Flags"
            else 
                field.Name

        // JSON Constructor
        let typeField = if ledgerType.IsTransaction then "TransactionType" else "LedgerEntryType"
        let baseCtor =  if ledgerType.IsTransaction then ": base(json)" else ""
        writer.WriteLine("        internal {0}(JsonElement json){1}", ledgerTypeClassName, baseCtor)
        writer.WriteLine("        {")
        writer.WriteLine("            if (json.GetProperty(\"{0}\").GetString() != \"{1}\")", typeField, ledgerType.Name)
        writer.WriteLine("            {")
        writer.WriteLine("                throw new ArgumentException(\"Expected property \\\"LedgerEntryType\\\" to be \\\"{0}\\\"\", \"json\");", ledgerType.Name);
        writer.WriteLine("            }")
        if ledgerType.Fields |> Seq.exists (fun field -> field.Optional || field.Type.StartsWith "Array<" || field.Type = "Vector256") then
            writer.WriteLine("            JsonElement element;")
        writer.WriteLine()
        for field in ledgerType.Fields do
            let fieldName = getFieldName field

            let writeArray (spacer : string) inner =
                writer.WriteLine("{0}var {1}Array = new {2}[element.GetArrayLength()];", spacer, field.Name, inner)
                writer.WriteLine("{0}for (int i = 0; i < {1}Array.Length; ++i)", spacer, field.Name)
                writer.WriteLine("{0}{{", spacer)
                writer.WriteLine("{0}    {1}Array[i] = {2};", spacer, field.Name, readJsonField field "element[i]")
                writer.WriteLine("{0}}}", spacer)
                writer.WriteLine("{0}{1} = Array.AsReadOnly({2}Array);", spacer, fieldName, field.Name)

            if field.Optional then
                writer.WriteLine("            if (json.TryGetProperty(\"{0}\", out element))", field.Name)
                writer.WriteLine("            {")
                match getInnerType field with
                | Some inner ->
                    writeArray "                " inner
                | None ->
                    writer.WriteLine("                {0} = {1};", fieldName, readJsonField field "element")
                writer.WriteLine("            }")
            else
                let reader = sprintf "json.GetProperty(\"%s\")" field.Name
                match getInnerType field with
                | Some inner ->
                    writer.WriteLine("            element = {0};", reader)
                    writeArray "            " inner
                | None ->
                    writer.WriteLine("            {0} = {1};", fieldName, readJsonField field reader)
        writer.WriteLine("        }")
        writer.WriteLine()

        // StReader Constructor (can't easily use a base constructor for Transactions here)
        writer.WriteLine("        internal {0}(ref StReader reader)", ledgerTypeClassName)
        writer.WriteLine("        {")        
        writer.WriteLine("            StFieldId fieldId = reader.ReadFieldId();")

        // Need to sort the fields based on type and field index
        let sortedFields = 
            allFields
            |> List.sortBy (fun field -> 
                let typeIndex = types.[field.OriginalType]
                typeIndex, field.Nth
            )
            |> List.toArray

        for i = 0 to sortedFields.Length - 1 do
            let field = sortedFields.[i]
            let fieldName = getFieldName field

            // Read the first field id
            let readNextField (spacer : string) =
                // Won't be any field if we're on the last field
                if i <> sortedFields.Length - 1 then                
                    writer.WriteLine("{0}if (!reader.TryReadFieldId(out fieldId))", spacer)
                    writer.WriteLine("{0}{{", spacer)
                    // If we couldn't find another field id but there are still non-optional fields then throw, else just early return
                    if sortedFields |> Seq.skip (i + 1) |> Seq.exists (fun field -> not field.Optional) then
                        writer.WriteLine("{0}    throw new Exception(\"End of st data reached but non-optional fields still not set\");", spacer)
                    else
                        writer.WriteLine("{0}    return;", spacer)
                    writer.WriteLine("{0}}}", spacer)

            let fieldId = sprintf "StFieldId.%s_%s" field.OriginalType field.Name

            let writeArray (spacer : string) (inner : string) =
                writer.WriteLine("{0}var {1}List = new System.Collections.Generic.List<{2}>();", spacer, field.Name, inner)
                writer.WriteLine("{0}while (true)", spacer)
                writer.WriteLine("{0}{{", spacer)
                writer.WriteLine("{0}    fieldId = reader.ReadFieldId();", spacer)
                writer.WriteLine("{0}    if (fieldId == StFieldId.Array_ArrayEndMarker)", spacer)
                writer.WriteLine("{0}    {{", spacer)
                readNextField (sprintf "%s        " spacer)
                writer.WriteLine("{0}        break;", spacer)
                writer.WriteLine("{0}    }}", spacer)
                writer.WriteLine("{0}    if (fieldId != StFieldId.Object_{1})", spacer, inner)
                writer.WriteLine("{0}    {{", spacer)
                writer.WriteLine("{0}        throw new Exception(string.Format(\"Expected {{0}} but got {{1}}\", StFieldId.Object_{1}, fieldId));", spacer, inner)
                writer.WriteLine("{0}    }}", spacer)


                writer.WriteLine("{0}    {1}List.Add({2});", spacer, field.Name, readStField field)
                writer.WriteLine("{0}}}", spacer)
                writer.WriteLine("{0}{1} = {2}List.AsReadOnly();", spacer, fieldName, field.Name)

            let writeField (spacer : string) =
                match getInnerType field with
                | Some inner ->
                    writeArray spacer inner
                | None ->
                    writer.WriteLine("{0}{1} = {2};", spacer, fieldName, readStField field)
                    readNextField spacer

            if field.Optional then
                writer.WriteLine("            if (fieldId == {0})", fieldId)
                writer.WriteLine("            {")
                if field.Type = "Vector256" then
                    // Looks like an array but isn't in ST format
                    writer.WriteLine("                {0} = Array.AsReadOnly({1});", fieldName, readStField field)
                    readNextField "            "

                else
                    writeField "                "
                writer.WriteLine("            }")
                
            else
                writer.WriteLine("            if (fieldId != {0})", fieldId)
                writer.WriteLine("            {")
                writer.WriteLine("                throw new Exception(string.Format(\"Expected {{0}} but got {{1}}\", {0}, fieldId));", fieldId)
                writer.WriteLine("            }")
                if field.Type = "Vector256" then
                    writer.WriteLine("            {0} = Array.AsReadOnly({1});", fieldName, readStField field)
                    readNextField "            "
                else
                    writeField "            "


        writer.WriteLine("        }")
        writer.WriteLine()

        // Transaction serialise method
        if ledgerType.IsTransaction then
            writer.WriteLine("        private protected override void Serialize(IBufferWriter<byte> bufferWriter, bool forSigning)")
            writer.WriteLine("        {")

            writer.WriteLine("            var writer = new StWriter(bufferWriter);")
            writer.WriteLine(
                "            writer.WriteTransactionType(StTransactionType.{0});", 
                ledgerType.Name)

            for i = 0 to sortedFields.Length - 1 do
                let field = sortedFields.[i]
                let fieldName = getFieldName field
                let stWriter = writeStField field
                let fieldCode = sprintf "St%sFieldCode.%s" field.OriginalType field.Name

                if fieldCode = "StUInt32FieldCode.Flags" then
                    // Special case flags for writing
                    writer.WriteLine("            if (base.Flags != 0u)");
                    writer.WriteLine("            {");
                    writer.WriteLine(
                        "                " + 
                        "writer.WriteUInt32(StUInt32FieldCode.Flags, base.Flags);")
                    writer.WriteLine("            }");
                else
                    let writeField (spacer : string) =
                        let writeArray (spacer : string) (fieldExpression : string) inner =  
                            writer.WriteLine(
                                "{0}writer.WriteStartArray({1});",
                                spacer, fieldCode)
                            writer.WriteLine(
                                "{0}foreach(var entry in {1})",
                                spacer, fieldExpression)
                            writer.WriteLine("{0}{{", spacer)
                            writer.WriteLine("{0}    {1};", 
                                spacer, 
                                String.Format(stWriter, "entry"))
                            writer.WriteLine("{0}}}", spacer)
                            writer.WriteLine("{0}writer.WriteEndArray();", spacer)

                        let writeField (spacer : string) (fieldExpression : string) =
                            match getInnerType field with
                            | Some inner ->
                                writeArray spacer fieldExpression inner
                            | None ->
                                writer.WriteLine(
                                    "{0}{1};",
                                    spacer,
                                    String.Format(stWriter, fieldCode, fieldExpression))

                        if field.Optional then
                            writer.WriteLine(
                                "{0}if ({1} != null)", spacer, fieldName)
                            writer.WriteLine("{0}{{", spacer)

                            let fieldExpression = 
                                if isValueType field then
                                    sprintf "%s.Value" fieldName
                                else
                                    fieldName

                            writeField (spacer + "    ") fieldExpression

                            writer.WriteLine("{0}}}", spacer)
                        else 

                            writeField spacer fieldName
                            
                    if (field.IsSigningField) then
                        writeField "            "
                    else
                        writer.WriteLine("            if (!forSigning)")
                        writer.WriteLine("            {")
                        writeField "                "
                        writer.WriteLine("            }")



            writer.WriteLine("        }")

        writer.WriteLine("    }")
        writer.WriteLine()

[<EntryPoint>]
let main argv =
    let stDefinitionsPath =
        Directory.GetCurrentDirectory()
        |> findPath

    printfn "Emiting definitions to %s" stDefinitionsPath

    let definitions = downloadDefinitions ()

    use writer = new StreamWriter(IO.File.Open(stDefinitionsPath, FileMode.Create, FileAccess.Write))

    writer.WriteLine("// This file is auto generated by GenerateDefinitions, do not edit by hand")
    writer.WriteLine("using System;")
    writer.WriteLine("using System.Buffers;")
    writer.WriteLine("using System.Collections.ObjectModel;")
    writer.WriteLine("using System.Text.Json;")
    writer.WriteLine("using Ibasa.Ripple.St;")
    
    writer.WriteLine("namespace Ibasa.Ripple.St")
    writer.WriteLine("{")

    emitTransactionType writer definitions
    emitLedgerEntry writer definitions
    emitTypes writer definitions
    emitFields writer definitions
    writer.WriteLine("}")

    
    writer.WriteLine("namespace Ibasa.Ripple")
    writer.WriteLine("{")
    
    emitLedger writer definitions
    
    writer.WriteLine("}")
    writer.WriteLine("")

    0