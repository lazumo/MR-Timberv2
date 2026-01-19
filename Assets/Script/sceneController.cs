public static class SceneController
{
    public static int CurrentLevel = 1;

    public static int GetCurrentStage()
    {
        return CurrentLevel;
    }

    public static void NextLevel()
    {
        CurrentLevel++;
    }

    public static void ResetLevel()
    {
        CurrentLevel = 1;
    }
}
