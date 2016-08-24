﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.IO;

//class - policy control
public class GameManager : MonoBehaviour
{
    public enum State { Waiting, Playing, End } // 로딩 대기, 게임 중, 게임 끝

	[SerializeField] int playerNumber;
	[SerializeField] bool[] checkAlly;
	[SerializeField] bool isAI;
    State gameState;                    // 현재 게임 상태
    MainManager mainManager;
    NetManager netManager;
    RoomInfo curRoomInfo;
    //UnitDatabase unitDatabase;
    UnitManager unitManager;
    Database dataBase;
    // Load 관련
    int LoadCompleteCount; // 몇명이나 로딩이 끝났는지

    //property
    public int PlayerNumber { get { return playerNumber; } }

	public bool[] CheckAlly { get { return checkAlly; } }

    void Awake()
    {
        //메인 매니저, 넷 매니저, 게임매니저 가져오기
        mainManager = MainManager.instance;
        netManager = mainManager.netManager;
        curRoomInfo = mainManager.currentRoomInfo;
        playerNumber = curRoomInfo.myNumber;
        //unitDatabase = new UnitDatabase("UnitData.data", FileMode.Open);
        dataBase = Database.Instance;
        unitManager = new UnitManager();

        // 초기 게임 상태 : 대기
        gameState = State.Waiting;

        // 리시버 등록
        netManager.RegisterReceiveNotificationP2P((int)P2PPacketType.LoadComplete, OnReceiveLoadComplete);
        netManager.RegisterReceiveNotificationP2P((int)P2PPacketType.StartGame, OnReceiveStartGame);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.CreateUnit, OnReceiveCreateUnit);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitCastSkill, OnReceiveUnitCastSkill);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitDamaged, OnReceiveUnitDamaged);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitDeath, OnReceiveUnitDeath);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitLevelUp, OnReceiveUnitLevelUp);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitSetDestination, OnReceiveUnitMove);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitImmediatelyMove, OnReceiveUnitImmediatelyMove);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitStop, OnReceiveUnitStop);
        netManager.RegisterReceiveNotificationP2P((int)InGamePacketID.UnitAttack, OnReceiveUnitAttack);
    }

    void OnDestroy()
    {
        // 리시버 해제
        netManager.UnRegisterReceiveNotificationP2P((int)P2PPacketType.LoadComplete);
        netManager.UnRegisterReceiveNotificationP2P((int)P2PPacketType.StartGame);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.CreateUnit);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitCastSkill);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitDamaged);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitDeath);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitLevelUp);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitSetDestination);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitImmediatelyMove);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitStop);
        netManager.UnRegisterReceiveNotificationP2P((int)InGamePacketID.UnitAttack);
    }

    //initialize this script
    void Start()
	{        
         StartCoroutine(GameLoading()); // 로딩 + 로딩완료검사
    }

    void InitializeData() 
	{
        // 동맹 배열
		checkAlly = new bool[8];
    	//checkAlly[playerNumber-1] = true; // 자기 자신은 true;

        // 플레이어는 전부 동맹
        List<PlayerInfo> listPlayer = curRoomInfo.GetAllGuestInfo();
        for(int i = 0; i < listPlayer.Count; i++)
        {
            checkAlly[listPlayer[i].number - 1] = true;
        }

        // 자신의 케릭터 생성하기
        InGameCreateUnitData data = new InGameCreateUnitData();
        
        data.level = 1;
        data.identity.unitOwner = (byte)playerNumber;
        data.unitType = 1;
        data.identity.unitId = 1;
        //TODO
        // 자신의 스타팅 포인트에 케릭터를 위치시킴. 해야되지만 테스트용으로 땜빵함.
        data.position = new Vector3(playerNumber * 2f, 0f, 0f);

        UnitCreate(data); //  유닛 생성
    }
    
    IEnumerator GameLoading() // 로딩 + 로딩완료 검사 루틴 - 호스트만 실행
    {
        yield return null; // 한 프레임 기다리기        

        InitializeData(); // 초기화

        // 자신이 호스트라면
        if (curRoomInfo.isHost)
        {
            LoadCompleteCount++;
        }
        else // 게스트라면 호스트에게 로딩 완료 메세지 전송
        {
            P2PLoadCompleteData loadCompleteData = new P2PLoadCompleteData();
            loadCompleteData.playerNumber = (byte)playerNumber;
            P2PLoadCompletePacket loadCompletePacket = new P2PLoadCompletePacket(loadCompleteData);
            netManager.SendToHost(loadCompletePacket);
        }
        if (curRoomInfo.isHost)
        {
            while (LoadCompleteCount < curRoomInfo.PlayerCount)
            {
                yield return null;
            }

            // 모두 로딩 완료

            // 게임 시작 메세지 전송
            P2PStartGamePacket packet = new P2PStartGamePacket();
            netManager.SendToAllGuest(packet);

            // 게임 상태 변경 
            gameState = State.Playing; // 게임중
        }
    }    

    // 자신의 유닛(호스트일 경우 AI 유닛 포함)을 생성하고 메세지를 전송한다.
    public GameObject UnitCreate(InGameCreateUnitData createUnitData)
    {
        // 유닛 생성이 가능한지 물어봄
        int unitId = unitManager.FindEmptySlot(createUnitData.identity.unitOwner);

        if(unitId < 0)
        {
            return null; // Full unit
        }
        createUnitData.identity.unitId = (byte)unitId;
        // TODO : 데이터베이스에 프리팹의 경로가 있어야 할듯!
        GameObject unitObj = Instantiate(Resources.Load<GameObject>("ProtoType1"), createUnitData.position, Quaternion.identity) as GameObject;

        // 유닛 타입 정보 얻기
        UnitData unitData = dataBase.GetUnitData(createUnitData.unitType);
        UnitLevelData unitLevelData = unitData.levelData[createUnitData.level];

        // 유닛 정보 초기화, 자신의 유닛은 UnitProcess를 붙인다.
        unitObj.AddComponent<UnitProcess>().SetUp(new UnitInformation(createUnitData, unitData, unitLevelData), createUnitData.position);

        // 유닛 매니져에 유닛 등록
        if (!unitManager.InsertSlot(unitObj, unitId))
        {
            Destroy(unitObj); // 실패시 삭제
            return null;
        }

        // 생성 결과를 다른 사람들에게 알림.
        InGameCreateUnitPacket packet = new InGameCreateUnitPacket(createUnitData);
        SendChangedData(packet);
        return unitObj;
    }

    public void UnitSetDestination (UnitProcess unit, Vector3 destination) // 목표지점 설정
    {
        InGameUnitSetDestinationData data = new InGameUnitSetDestinationData();
        InGameUnitSetDestinationPacket packet = new InGameUnitSetDestinationPacket(data);
        SendChangedData(packet);
    }    

    public void UnitSetTarget(UnitProcess sourceUnit, GameObject targetUnit) // 목표 설정
    {
        InGameUnitSetTargetData data = new InGameUnitSetTargetData();
        InGameUnitSetTargetPacket packet = new InGameUnitSetTargetPacket(data);
        SendChangedData(packet);
    }

    public void UnitImmediateMove(UnitProcess unit, Vector3 position) // 해당지점으로 즉시 이동
    {
        InGameUnitImmediatlyMoveData data = new InGameUnitImmediatlyMoveData();
        InGameUnitImmediatelyMovePacket packet = new InGameUnitImmediatelyMovePacket(data);
        SendChangedData(packet);
    }

    public void UnitAttack(UnitProcess sourceUnit, GameObject targetUnit) // 자신의 유닛 공격
    {
        InGameUnitAttackData data = new InGameUnitAttackData();
        InGameUnitAttackPacket packet = new InGameUnitAttackPacket(data);
        SendChangedData(packet);
    }

    public void UnitCastSkill(UnitProcess unit, Skill skill)    // 스킬 시전
    {
        InGameUnitCastSkillData data = new InGameUnitCastSkillData();
        InGameUnitCastSkillPacket packet = new InGameUnitCastSkillPacket(data);
        SendChangedData(packet);
    }

    public void UnitStop(UnitProcess unit) // 유닛 멈춤 또는 홀드
    {
        InGameUnitStopData data = new InGameUnitStopData();
        InGameUnitStopPacket packet = new InGameUnitStopPacket(data);
        SendChangedData(packet);
    }

    public void UnitLevelUp(UnitProcess unit, int level) // 레벨업
    {
        InGameUnitLevelUpData data = new InGameUnitLevelUpData();
        InGameUnitLevelUpPacket packet = new InGameUnitLevelUpPacket(data);
        SendChangedData(packet);
    }

    public void UnitDamaged(UnitProcess unit, int damage) // 피해입음
    {
        InGameUnitDamagedData data = new InGameUnitDamagedData();
        InGameUnitDamagedPacket packet = new InGameUnitDamagedPacket(data);
        SendChangedData(packet);
    }

    public void UnitDeath(UnitProcess unit) // 죽음
    {
        InGameUnitDeathData data = new InGameUnitDeathData();
        InGameUnitDeathPacket packet = new InGameUnitDeathPacket(data);
        SendChangedData(packet);        
    }    

    // 패킷을 전송할 대상을 정해 전송하는 메서드
    void SendChangedData<T>(IPacket<T> packet)
    {
        if (curRoomInfo.isHost) // 자신이 호스트라면
        {
            netManager.SendToAllGuest(packet); // 모든 게스트에게 보낸다.
        }
        else // 자신이 게스트라면
        {
            netManager.SendToHost(packet); // 호스트에게 보낸다.
        }
    }

    // 유닛 생성 수신 리시버 [ 게스트 -> 호스트 ]
    void OnReceiveCreateUnit(Socket client, byte[] data)
    {
        InGameCreateUnitPacket packet = new InGameCreateUnitPacket(data);
        InGameCreateUnitData createUnitData = packet.GetData();

        // TODO : 데이터베이스에 프리팹의 경로가 있어야 할듯!
        GameObject unitObj = Instantiate(Resources.Load<GameObject>("ProtoType1"), createUnitData.position, Quaternion.identity) as GameObject;

        // 유닛 타입 정보 얻기
        UnitData unitData = dataBase.GetUnitData(createUnitData.unitType);
        UnitLevelData unitLevelData = unitData.levelData[createUnitData.level];

        // 유닛 정보 초기화, 다른사람의 유닛은 UnitPlayer 스크립트를 붙인다.
        unitObj.AddComponent<UnitPlayer>().SetUp(new UnitInformation(createUnitData, unitData, unitLevelData), createUnitData.position);

        // 유닛 매니져에 유닛 등록
        unitManager.InsertSlot(unitObj, createUnitData.identity.unitId);
        
        // 자신이 호스트라면
        if (curRoomInfo.isHost)
        {
            // 모두에게 전송, 너만빼고
            PlayerInfo player = curRoomInfo.GetGuestInfo(createUnitData.identity.unitOwner);
            netManager.SendToAllGuest(client, packet);
        }
    }

    // 로딩 끝 패킷 수신 메서드 [ 게스트 -> 호스트 ]
    void OnReceiveLoadComplete(Socket client, byte[] data)
    {
        P2PLoadCompletePacket packet = new P2PLoadCompletePacket(data);
        Debug.Log(curRoomInfo.GetGuestInfo(packet.GetData().playerNumber).playerName + "로딩끝");
        // 로딩 완료 인원 증가
        LoadCompleteCount++;
    }

    // 게임 시작 패킷 수신 메서드 [ 호스트 -> 게스트 ]
    void OnReceiveStartGame(Socket client, byte[] data)
    {
        // TODO
        // 카메라 이벤트 실행
        Debug.Log("게임시작");
        gameState = State.Playing;
    }    
    
    // 스킬 사용 리시버 [ 게스트 -> 호스트, 호스트 -> 모든게스트 ]
    void OnReceiveUnitCastSkill(Socket client, byte[] data)
    {
        InGameUnitCastSkillPacket packet = new InGameUnitCastSkillPacket(data);
        InGameUnitCastSkillData castSkillData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 피해 리시버 [ 게스트 -> 호스트, 호스트 -> 모든게스트 ]
    void OnReceiveUnitDamaged(Socket client, byte[] data)
    {
        InGameUnitDamagedPacket packet = new InGameUnitDamagedPacket(data);
        InGameUnitDamagedData damagedData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 죽음 리시버 [ 게스트 -> 호스트, 호스트 -> 모든게스트 ]
    void OnReceiveUnitDeath(Socket client, byte[] data)
    {
        InGameUnitDeathPacket packet = new InGameUnitDeathPacket(data);
        InGameUnitDeathData deathData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 레벨업 리시버[게스트->호스트, 호스트->모든게스트]
    void OnReceiveUnitLevelUp(Socket client, byte[] data)
    {
        InGameUnitLevelUpPacket packet = new InGameUnitLevelUpPacket(data);
        InGameUnitLevelUpData levelUpData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 이동 리시버 [ 게스트 -> 호스트, 호스트 -> 모든게스트 ]
    void OnReceiveUnitMove(Socket client, byte[] data)
    {
        InGameUnitSetDestinationPacket packet = new InGameUnitSetDestinationPacket(data);
        InGameUnitSetDestinationData moveData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 즉시 이동 리시버 [ 게스트 -> 호스트, 호스트 -> 모든게스트 ]
    void OnReceiveUnitImmediatelyMove(Socket client, byte[] data)
    {
        InGameUnitImmediatelyMovePacket packet = new InGameUnitImmediatelyMovePacket(data);
        InGameUnitImmediatlyMoveData moveData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 멈춤 리시버[게스트->호스트, 호스트->모든게스트]
    void OnReceiveUnitStop(Socket client, byte[] data)
    {
        InGameUnitStopPacket packet = new InGameUnitStopPacket(data);
        InGameUnitStopData stopData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }

    // 유닛 공격 [게스트 -> 호스트, 호스트 -> 모든게스트]
    void OnReceiveUnitAttack(Socket client, byte[] data)
    {
        InGameUnitAttackPacket packet = new InGameUnitAttackPacket(data);
        InGameUnitAttackData stopData = packet.GetData();
        netManager.SendToAllGuest(client, packet);
    }
}
