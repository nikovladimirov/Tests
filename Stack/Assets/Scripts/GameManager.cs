﻿using System;
using System.Collections.Generic;
using System.Linq;
using Behaviours;
using Data;
using DefaultNamespace.Enums;
using Helpers;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DefaultNamespace
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        
        #region Events

        public event GameStateChangedArgs GameStateChanged;
        public event GameScoreChangedArgs GameScoreChanged;
        public event GameTopScoreChangedArgs GameTopScoreChanged;

        #endregion Events
        
        private const string TopScoreConst = "TopScore";
        private const string BuildJsonConst = "BuildJson";
        private const double Tolerance = 0.001;

        [SerializeField] private GameObject _cubePrefab;
        [SerializeField] private GameObject _trashCubePrefab;
        [SerializeField] private GameObject _gameObjects;
        [SerializeField] private GameObject _gui;
        [SerializeField] public float StartPosition;
        [SerializeField] public float InitialCubeSpeed;
        
        [SerializeField] private List<Color> _colors;

        private GameState _gameState;
        
        private GameObject _parentCubes;
        private GameObject _parentTrash;
        
        private CubeBehaviour _firstCube;
        private CubeBehaviour _lastCube;
        
        private int _score;
        private int _topScore;

        private Vector3 _defaultCameraPosition;
        private float _defaultFieldOfView;
        private bool _moveCameraEndOfGame;

        private Color _nextColor;
        private int _changeColorPartIndex;
        
        private Vector3 _groundCamOffset;
        private Vector3 _camTarget;

        private List<CubeBehaviour> _cubes = new List<CubeBehaviour>();

        public float CubeSpeed { get; private set; }
        
        public int TopScore => _topScore;

        private void Awake()
        {
            if (Instance != null)
                throw  new Exception("already initialized");
            
            Instance = this;

            _gui.SetActive(true);
            _topScore = PlayerPrefs.GetInt(TopScoreConst, 0);
            OnGameTopScoreChanged();
            _defaultCameraPosition = Camera.main.transform.position;
            _defaultFieldOfView = Camera.main.fieldOfView;

            Vector3 groundPos = GetWorldPosAtViewportPoint(0.5f, 0.5f);
            _groundCamOffset = Camera.main.transform.position - groundPos;
            _camTarget = Camera.main.transform.position;
            
            BuildLastTower();
        }
        
        public delegate void GameStateChangedArgs(GameState state);
        public delegate void GameScoreChangedArgs(int score);
        public delegate void GameTopScoreChangedArgs(int topscore);

        private void BuildLastTower()
        {
            try
            {
                var jsonPrevBuild = PlayerPrefs.GetString(BuildJsonConst, string.Empty);
                var cubes = JsonHelper.DeserializeFromString<List<CubeData>>(jsonPrevBuild);
                if(cubes == default)
                    return;
                
                _cubes.Clear();
                if (_parentCubes != null)
                    Destroy(_parentCubes);
                
                _parentCubes = new GameObject("Cubes");
                _parentCubes.transform.SetParent(_gameObjects.transform);

                _moveCameraEndOfGame = true;
                
                var first = true;
                foreach (var cubeData in cubes)
                {
                    _lastCube = SpawnCube();
                    _lastCube.Init(cubeData.Position, cubeData.Scale, ColorConverter.HexToColor(cubeData.Color) ?? Color.white);
                    if (first)
                    {
                        _firstCube = _lastCube;
                        first = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private Vector3 GetWorldPosAtViewportPoint(float vx, float vy)
        {
            Ray worldRay = Camera.main.ViewportPointToRay(new Vector3(vx, vy, 0));
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            float distanceToGround;
            groundPlane.Raycast(worldRay, out distanceToGround);
            Debug.Log("distance to ground:" + distanceToGround);
            return worldRay.GetPoint(distanceToGround);
        }

        public void SetGameState(GameState newState)
        {
            switch (_gameState)
            {
                case GameState.Menu:
                    if (newState != GameState.Playing)
                        return;
                    break;
                case GameState.Playing:
                    if (newState == GameState.Playing)
                        return;
                    break;
                case GameState.Death:
                    if (newState != GameState.Menu)
                        return;
                    break;
                default:
                    return;
            }

            _gameState = newState;

            switch (newState)
            {
                case GameState.Playing:
                    if (_parentCubes != null)
                        Destroy(_parentCubes);

                    _parentCubes = new GameObject("Cubes");
                    _parentCubes.transform.SetParent(_gameObjects.transform);

                    if (_parentTrash != null)
                        Destroy(_parentTrash);

                    _parentTrash = new GameObject("Trash");
                    _parentTrash.transform.SetParent(_gameObjects.transform);

                    _moveCameraEndOfGame = true;
                    _score = -1;
                    _cubes.Clear();

                    _nextColor = _colors[Random.Range(0, _colors.Count)];
                    Camera.main.fieldOfView = _defaultFieldOfView;
                    Camera.main.transform.position = _defaultCameraPosition;

                    CubeSpeed = InitialCubeSpeed;
                    OnGameScoreChanged();
                    SpawnFirstCube();
                    SpawnNextCube();

                    break;
                case GameState.Death:
                    if (_score > _topScore)
                    {
                        PlayerPrefs.SetInt(TopScoreConst, _score);
                        _topScore = _score;
                        OnGameTopScoreChanged();
                    }

                    PlayerPrefs.SetString(BuildJsonConst,
                        JsonHelper.SerializeToString(_cubes.Where(x => x.WithScore == x || x.WithScore == null)
                            .Select(x => new CubeData(x)).ToList()));
                    PlayerPrefs.Save();

                    var y = (_lastCube.transform.position.y + _firstCube.transform.position.y) / 2;
                    Camera.main.transform.position = new Vector3(_defaultCameraPosition.x, _defaultCameraPosition.y + y,
                        _defaultCameraPosition.z);

                    break;
            }

            OnGameStateChanged();
        }

        private void OnGameStateChanged()
        {
            var hanler = GameStateChanged;
            if (hanler == null)
                return;

            Debug.Log($"GameState: {_gameState}");
            hanler(_gameState);
        }

        private void OnGameScoreChanged()
        {
            var hanler = GameScoreChanged;
            if (hanler == null)
                return;

            hanler(_score);
        }

        private void OnGameTopScoreChanged()
        {
            var hanler = GameTopScoreChanged;
            if (hanler == null)
                return;

            hanler(_topScore);
        }

        public void SpawnFirstCube()
        {
            var cube = SpawnCube();
            cube.Init(null, GenerateColor());
            _lastCube = cube;
            _firstCube = cube;
        }

        public TrashCubeBehaviour SpawnTrash(Vector3 position, Vector3 size, Color color)
        {
            var go = Instantiate(_trashCubePrefab);
            go.transform.SetParent(_parentTrash.transform);
            var cube = go.GetComponent<TrashCubeBehaviour>();
            cube.Init(position, size, color);
            return cube;
        }

        private CubeBehaviour SpawnCube()
        {
            var go = Instantiate(_cubePrefab);
            go.transform.SetParent(_parentCubes.transform);
            var cube = go.GetComponent<CubeBehaviour>();
            _cubes.Add(cube);
            return cube;
        }

        private Color GenerateColor()
        {
            switch (_changeColorPartIndex)
            {
                case 0:
                    _nextColor = new Color(_nextColor.r + 0.1f, _nextColor.g, _nextColor.b, _nextColor.a);
                    break;
                case 1:
                    _nextColor = new Color(_nextColor.r, _nextColor.g + 0.1f, _nextColor.b, _nextColor.a);
                    break;
                case 2:
                    _nextColor = new Color(_nextColor.r, _nextColor.g, _nextColor.b + 0.1f, _nextColor.a);
                    break;
            }

            return _nextColor;
        }

        public void SpawnNextCube()
        {
            _score++;
            OnGameScoreChanged();

            _camTarget = _lastCube.transform.position + _groundCamOffset;

            CheckNextLevel();

            var cube = SpawnCube();
            cube.Init(_lastCube, GenerateColor());

            _lastCube = cube;
        }

        private bool CheckNextLevel()
        {
            if (_score % 6 == 5)
            {
                CubeSpeed *= 1.03f;
                _nextColor = _colors[Random.Range(0, _colors.Count)];
                _changeColorPartIndex = Random.Range(0, 3);
                switch (_changeColorPartIndex)
                {
                    case 0:
                        if (Math.Abs(_nextColor.r) > Tolerance)
                            _changeColorPartIndex = (Math.Abs(_nextColor.g) > Tolerance) ? 2 : 1;
                        break;
                    case 1:
                        if (Math.Abs(_nextColor.r) > Tolerance)
                            _changeColorPartIndex = (Math.Abs(_nextColor.r) > Tolerance) ? 2 : 0;
                        break;
                    case 2:
                        if (Math.Abs(_nextColor.r) < Tolerance)
                            _changeColorPartIndex = (Math.Abs(_nextColor.r) > Tolerance) ? 1 : 0;
                        break;
                }
            }

            return false;
        }

        private void Update()
        {
            if (_lastCube == null)
                return;

            switch (_gameState)
            {
                case GameState.Playing:
                    if (Input.GetMouseButtonDown(0))
                    {
                        _lastCube.Drop();
                        return;
                    }

                    _lastCube.NextPosition();
                    if (Camera.main != null && Math.Abs(_camTarget.y) > Tolerance &&
                        Math.Abs(Camera.main.transform.position.y - _camTarget.y) > Tolerance)
                        Camera.main.transform.position =
                            Vector3.Lerp(Camera.main.transform.position, _camTarget, Time.deltaTime * 1f);
                    break;

                case GameState.Menu:
                case GameState.Death:
                    if (Camera.main == null || IsTargetVisible(_lastCube.WithScore.gameObject) &&
                        IsTargetVisible(_firstCube.gameObject))
                    {
                        if (_moveCameraEndOfGame)
                        {
                            _moveCameraEndOfGame = false;
                            Camera.main.fieldOfView += 10;
                            var p = Camera.main.transform.position;
                            Camera.main.transform.position =
                                Vector3.Lerp(Camera.main.transform.position, new Vector3(p.x, p.y + 5, p.z),
                                    Time.deltaTime * 1f);

                            _lastCube = null;
                        }

                        return;
                    }

                    Camera.main.fieldOfView += 5;
                    break;
            }
        }

        private bool IsTargetVisible(GameObject go)
        {
            if (go == null)
                return true;

            var c = Camera.main;
            var planes = GeometryUtility.CalculateFrustumPlanes(c);
            var point = go.transform.position;
            foreach (var plane in planes)
            {
                if (plane.GetDistanceToPoint(point) < 0)
                    return false;
            }

            return true;
        }
    }
}